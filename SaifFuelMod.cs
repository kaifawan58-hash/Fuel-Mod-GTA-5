// SaifFuelMod.cs
// C# / ScriptHookVDotNet3 port of the original Lua fuel mod. Keeps the same
// systems and controls as the source:
//  - Approach a pump -> E opens the service menu -> W/S move -> A/D adjust
//    fuel amount -> E confirms/pays -> Q closes. Items: Fuel, Engine Oil,
//    Transmission Oil, Repair Vehicle.
//  - X toggles the vehicle radio on/off.
//  - F stores the current vehicle's fuel into one of 4 remembered slots (so
//    up to 4 vehicles keep independent fuel levels, cycling slot each press).
//  - Out of fuel -> H calls emergency fuel delivery for an extra fee.
//  - F7 opens a runtime config menu (rates, prices) with the same
//    W/S/A/D/E controls, saved to disk.
// Real per-machine station coordinates, dynamic hour-of-day pricing,
// permanent speedometer, ambient refuelling traffic, and save/load are kept
// from the earlier additions discussed for this mod.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using Font = GTA.UI.Font;
using Control = GTA.Control;
using Screen = GTA.UI.Screen;

namespace SaifFuelMod
{
    public class FuelMod : Script
    {
        // =================================================================
        // STATION DATA - real per-machine coordinates
        // =================================================================
        private class Station
        {
            public string Name;
            public Dictionary<string, Vector3> Machines = new Dictionary<string, Vector3>();
            public bool IsMountain;           // region - affects price band
            public float RegionPriceMult = 1f; // randomized once per station
            public bool OutOfStock;
            public DateTime OutOfStockUntil = DateTime.MinValue;
        }

        private readonly List<Station> _stations = new List<Station>();

        private void BuildStations()
        {
            void Add(string name, params (string type, float x, float y, float z)[] machines)
            {
                var s = new Station { Name = name };
                foreach (var m in machines) s.Machines[m.type] = new Vector3(m.x, m.y, m.z);

                // Mountain/rural stations (higher elevation) cost more than city ones,
                // and each station gets its own small random variance so no two
                // stations are ever priced exactly the same - like the reference mod's
                // per-pump price fluctuation.
                float avgZ = 0f;
                foreach (var mv in s.Machines.Values) avgZ += mv.Z;
                avgZ /= Math.Max(1, s.Machines.Count);
                s.IsMountain = avgZ > 90f;
                s.RegionPriceMult = s.IsMountain
                    ? 1.10f + (float)Rand.NextDouble() * 0.20f   // 1.10x - 1.30x out in the hills
                    : 0.95f + (float)Rand.NextDouble() * 0.15f;  // 0.95x - 1.10x in the city

                _stations.Add(s);
            }

            Add("Saif Fuel - Cypress Flats",
                ("Regular", 1207.9f, -1397.8f, 35.3f), ("Diesel", 1203.99f, -1401.95f, 35.3f),
                ("Premium", 1213.2f, -1403.1f, 35.1f), ("Electric", 1209.15f, -1407.1f, 35.1f));
            Add("Saif Fuel - La Mesa",
                ("Regular", 818.38f, -1025.7f, 26.4f), ("Diesel", 818.22f, -1031.55f, 26.4f),
                ("Premium", 818.4f, -1025.7f, 26.4f), ("Electric", 818.3f, -1031.5f, 26.4f));
            Add("Saif Fuel - Strawberry",
                ("Regular", 265.68f, -1261.15f, 29.3f), ("Diesel", 270.04f, -1264.97f, 29.3f),
                ("Premium", 261.32f, -1257.35f, 29.3f), ("Electric", 265.7f, -1261.1f, 29.3f));
            Add("Saif Fuel - Elysian Island",
                ("Regular", -71.78f, -1765.62f, 29.5f), ("Diesel", -68.75f, -1757.55f, 29.5f),
                ("Premium", -71.5f, -1765.4f, 29.5f), ("Electric", -68.9f, -1758.2f, 29.5f));
            Add("Saif Fuel - Davis",
                ("Regular", -527.6f, -1204.8f, 18.3f), ("Diesel", -519.2f, -1208.75f, 18.3f),
                ("Premium", -531.4f, -1212.5f, 18.3f), ("Electric", -522.8f, -1216.65f, 18.3f));
            Add("Saif Fuel - Little Seoul",
                ("Regular", -723.42f, -932.5f, 19.2f), ("Diesel", -723.42f, -939.55f, 19.2f),
                ("Premium", -723.4f, -939.4f, 19.2f), ("Electric", -723.8f, -932.5f, 19.2f));
            Add("Saif Fuel - Del Perro",
                ("Regular", -2096.63f, -324.47f, 13.1f), ("Diesel", -2092.2f, -323.82f, 13.1f),
                ("Premium", -2100.15f, -315.62f, 13.1f), ("Electric", -2095.27f, -311.98f, 13.1f));
            Add("Saif Fuel - Tongva Hills",
                ("Regular", -1801.9f, 806.35f, 138.6f), ("Diesel", -1796.8f, 800.8f, 138.6f),
                ("Premium", -1796.8f, 800.95f, 138.6f), ("Electric", -1801.85f, 806.5f, 138.6f));
            Add("Saif Fuel - Rockford Hills",
                ("Regular", -1428.6f, -278.8f, 46.4f), ("Diesel", -1437.5f, -268.45f, 46.4f),
                ("Premium", -1435.05f, -284.3f, 46.4f), ("Electric", -1444.05f, -273.85f, 46.4f));
            Add("Saif Fuel - Vinewood",
                ("Regular", 621.6f, 273.8f, 103.3f), ("Diesel", 621.6f, 263.8f, 103.3f),
                ("Premium", 621.5f, 273.9f, 103.3f), ("Electric", 621.55f, 263.8f, 103.3f));
            Add("Saif Fuel - Mirror Park",
                ("Regular", 1180.85f, -329.65f, 69.3f), ("Diesel", 1181.0f, -329.67f, 69.3f),
                ("Premium", 1184.75f, -329.1f, 69.3f), ("Electric", 1177.35f, -330.45f, 69.3f));
            Add("Saif Fuel - Fort Zancudo Rd",
                ("Regular", 2580.5f, 361.68f, 108.6f), ("Diesel", 2580.47f, 361.68f, 108.6f),
                ("Premium", 2580.5f, 364.35f, 108.6f), ("Electric", 2580.35f, 358.9f, 108.6f));
            Add("Saif Fuel - Tataviam Mtns",
                ("Regular", 2677.8f, 3261.35f, 55.4f), ("Electric", 2681.5f, 3267.4f, 55.4f));
            Add("Saif Fuel - Grand Senora",
                ("Regular", 2009.66f, 3776.2f, 32.4f), ("Diesel", 2006.64f, 3774.33f, 32.4f),
                ("Premium", 2001.2f, 3771.35f, 32.4f), ("Electric", 2004.28f, 3772.8f, 32.4f));
            Add("Saif Fuel - Route 68",
                ("Regular", 1043.2f, 2674.95f, 39.7f), ("Diesel", 1035.4f, 2674.95f, 39.7f),
                ("Premium", 1043.0f, 2668.5f, 39.7f), ("Electric", 1035.6f, 2668.4f, 39.7f));
            Add("Saif Fuel - Harmony",
                ("Regular", 51.5f, 2776.5f, 58.0f), ("Diesel", 47.23f, 2780.2f, 58.0f));
            Add("Saif Fuel - Chumash",
                ("Regular", -2558.34f, 2333.5f, 33.2f), ("Diesel", -2551.85f, 2333.9f, 33.2f),
                ("Premium", -2558.5f, 2333.5f, 33.2f), ("Electric", -2552.6f, 2333.9f, 33.2f));
            Add("Saif Fuel - Sandy Shores",
                ("Regular", 1683.9f, 4931.9f, 42.2f), ("Diesel", 1689.6f, 4928.5f, 42.2f));
            Add("Saif Fuel - Paleto Bay",
                ("Regular", 1705.5f, 6414.0f, 32.7f), ("Diesel", 1701.45f, 6415.9f, 32.7f),
                ("Electric", 1697.4f, 6417.85f, 32.7f));
            Add("Saif Fuel - Mount Chiliad",
                ("Regular", 171.8f, 6603.6f, 32.0f), ("Diesel", 179.0f, 6604.85f, 32.0f),
                ("Premium", 186.4f, 6606.1f, 32.0f));
        }

        // =================================================================
        // RUNTIME CONFIG (editable in-game via F7, mirrors the
        // original's tunable "benfcrate/benocrate/benfspe/..." values)
        // =================================================================
        private float _fuelConsumeRate = 1.0f;      // multiplier on base burn rate
        private float _oilConsumeRate = 1.0f;       // multiplier on oil degrade rate
        private float _refuelSpeed = 4.0f;          // litres/second at the pump
        private float _oilChangeSpeed = 40.0f;      // units/second
        private bool _oilAffectsEngine = true;
        private float _priceRegular = 1.50f;
        private float _priceDiesel = 1.42f;
        private float _pricePremium = 1.65f;
        private float _priceElectric = 0.45f;
        private const string ConfigPath = "scripts\\SaifFuelMod_config.txt";

        // =================================================================
        // VEHICLE STATE
        // =================================================================
        private float _fuel = 30f;
        private float _maxFuel = 60f;
        private float _engineOil = 200f;
        private const float ENGINE_OIL_MAX = 200f;
        private float _transOil = 250f;
        private const float TRANS_OIL_MAX = 250f;
        private const string SavePath = "scripts\\SaifFuelMod_save.txt";
        // key = plate, value = fuel|engineOil|transOil
        private readonly Dictionary<string, float[]> _savedByPlate = new Dictionary<string, float[]>();
        private int _lastVehicleHandle = -1;
        private bool _radioOn = true;
        private bool _outOfFuelEmergencyPending = false;
        private DateTime _emergencyArrival = DateTime.MinValue;

        // 4-slot "remember this vehicle's fuel" system (mirrors ingatsatu/ingatdua/
        // ingattiga/ingatempat in the original) - lets up to 4 vehicles keep
        // independently tracked fuel without needing the plate save file.
        private int _money = 5000;

        // =================================================================
        // SERVICE MENU STATE (E opens, W/S navigate, A/D adjust, E confirms, Q closes)
        // =================================================================
        private enum ServiceItem { Fuel, EngineOil, TransOil, Repair, Close }
        private bool _menuOpen = false;
        private int _menuIndex = 0;
        private float _fuelRequested = 20f;
        private Station _nearStation = null;
        private string _nearFuelType = null;

        private enum ActiveJob { None, Refuel, ChangeEngineOil, ChangeTransOil, Repair }
        private ActiveJob _activeJob = ActiveJob.None;
        private float _jobTargetFuel = 0f;

        // =================================================================
        // CONFIG MENU STATE (F7 opens, W/S/A/D/E/Q controls)
        // =================================================================
        private bool _configOpen = false;
        private int _configIndex = 0;
        private const int CONFIG_ITEM_COUNT = 12;

        // HUD customization - editable from the F7 settings menu
        private enum HudStyle { VerticalBar, DigitalPercent }
        private HudStyle _hudStyle = HudStyle.VerticalBar;
        private enum HudCorner { BottomLeft, BottomRight, TopLeft, TopRight }
        private HudCorner _hudCorner = HudCorner.BottomLeft;
        private float _hudScale = 1.0f; // 0.6 - 1.6
        private const string HudConfigPath = "scripts\\SaifFuelMod_hud.txt";

        private static readonly Random Rand = new Random();

        private readonly List<Vehicle> _ambientVehicles = new List<Vehicle>();
        private readonly List<Ped> _ambientPeds = new List<Ped>();
        private const int AMBIENT_TARGET_COUNT = 3;
        private DateTime _lastAmbientCheck = DateTime.MinValue;

        // temporary consumption discount granted after an emergency H fuel call
        private DateTime _slowConsumptionUntil = DateTime.MinValue;

        // Fuel theft - walk up to a parked vehicle and a panel appears on its own
        // (same as the pump panel), Numpad5 confirms. No dedicated key.
        private bool _theftMenuOpen = false;
        private Vehicle _theftTarget = null;

        private bool _stationsSpawned = false;

        public FuelMod()
        {
            BuildStations();
            LoadSaveData();
            LoadConfig();
            LoadHudConfig();

            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += (s, e) => SaveData();

            Notification.Show("~g~Saif Fuel Mod~w~ loaded.");
            Notification.Show("~y~Approach a pump~w~ - the panel opens on its own, ~b~Numpad5~y~ confirms.");
            Notification.Show("~y~X~w~=radio  ~y~H~w~=emergency full tank  ~y~F7~w~=settings/HUD customize");
        }

        // =================================================================
        // TICK
        // =================================================================
        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                if (!_stationsSpawned)
                {
                    // NOTE: blips are safe here (ADD_BLIP_FOR_COORD is a plain native
                    // call), but ambient peds/vehicles use World.CreatePed/CreateVehicle,
                    // which call Model.Request() -> Script.Yield() internally. Yielding
                    // is illegal before the script's main loop starts, so all of that is
                    // deferred to the first Tick instead of the constructor.
                    foreach (var st in _stations) CreateStationBlips(st);
                    _stationsSpawned = true;
                }

                CheckVehicleSwitch();
                UpdateFuelAndOilDrain();
                AutoRememberFuel();
                UpdateHud();
                UpdateStationStock();
                UpdatePumpProximity();
                UpdateTheftProximity();
                UpdateActiveJob();
                UpdateEmergencyFuel();
                UpdateAmbientVehicles();

                if (_configOpen) DrawConfigMenu();
            }
            catch (Exception ex)
            {
                Log("OnTick error: " + ex.Message);
            }
        }

        // Continuously keeps this vehicle's fuel/oil remembered - no need to
        // press a key. Cheap in-memory update every tick; the on-disk file is
        // only written on job completion / vehicle switch / exit (see SaveData).
        private void AutoRememberFuel()
        {
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) return;
            Vehicle veh = playerPed.CurrentVehicle;
            if (veh == null || !veh.Exists()) return;

            string plate = Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, veh);
            if (string.IsNullOrEmpty(plate)) return;
            _savedByPlate[plate] = new[] { _fuel, _engineOil, _transOil };
        }

        private void CreateStationBlips(Station st)
        {
            foreach (var kvp in st.Machines)
            {
                Blip b = World.CreateBlip(kvp.Value);
                b.Sprite = BlipSprite.JerryCan;
                b.Color = BlipColor.Green;
                b.Name = st.Name + " - " + kvp.Key;
                b.IsShortRange = true;
            }
        }

        // =================================================================
        // FUEL + OIL DRAIN
        // =================================================================
        private void UpdateFuelAndOilDrain()
        {
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) return;
            Vehicle veh = playerPed.CurrentVehicle;
            if (veh == null || veh.Driver != playerPed) return;

            _maxFuel = GetTankSize(veh);
            if (_activeJob != ActiveJob.None) return; // don't burn fuel/oil mid-service

            if (veh.IsEngineRunning)
            {
                float speedKmh = veh.Speed * 3.6f;
                float modifier = speedKmh < 5f ? 1.2f : (speedKmh > 100f ? 0.8f : 1.0f);
                if (veh.ClassType == VehicleClass.Sports || veh.ClassType == VehicleClass.Super) modifier *= 1.2f;
                if (veh.EngineHealth < 700f) modifier *= 1.3f;
                modifier = Math.Min(modifier, 1.8f) * _fuelConsumeRate;

                // reward for calling emergency fuel - burns slower for a while afterward
                if (DateTime.Now < _slowConsumptionUntil) modifier *= 0.5f;

                float litresPerSecond = (8.4f / 100f) * modifier;
                _fuel = Math.Max(0f, _fuel - litresPerSecond * Game.LastFrameTime);

                float oilDrain = 0.03f * _oilConsumeRate * Game.LastFrameTime;
                _engineOil = Math.Max(0f, _engineOil - oilDrain);
                _transOil = Math.Max(0f, _transOil - oilDrain);

                if (_oilAffectsEngine && (_engineOil <= ENGINE_OIL_MAX * 0.15f || _transOil <= TRANS_OIL_MAX * 0.15f))
                {
                    veh.EngineHealth -= 0.05f * Game.LastFrameTime;
                }

                if (_fuel <= 0f)
                {
                    veh.IsEngineRunning = false;
                    Screen.ShowSubtitle("~r~Out of fuel!~w~ Press ~b~H~w~ for emergency delivery.", 3000);
                }
            }
        }

        private float GetTankSize(Vehicle veh)
        {
            switch (veh.ClassType)
            {
                case VehicleClass.Sports:
                case VehicleClass.Super: return 80f;
                case VehicleClass.Industrial:
                case VehicleClass.Service:
                case VehicleClass.Commercial: return 150f;
                case VehicleClass.Motorcycles: return 20f;
                default: return 60f;
            }
        }

        // =================================================================
        // OUT-OF-FUEL EMERGENCY DELIVERY (H key) - mirrors smf.benhabh
        // =================================================================
        private void UpdateEmergencyFuel()
        {
            if (!_outOfFuelEmergencyPending) return;
            if (DateTime.Now < _emergencyArrival) return;

            _fuel = _maxFuel; // full tank, not just half
            _slowConsumptionUntil = DateTime.Now.AddMinutes(3); // burns at half rate for 3 minutes as a bonus
            _outOfFuelEmergencyPending = false;
            Ped playerPed = Game.Player.Character;
            if (playerPed.IsInVehicle()) playerPed.CurrentVehicle.IsEngineRunning = false; // still needs restarting manually
            Notification.Show("~g~Emergency fuel delivered!~w~ Full tank + slower burn rate for 3 minutes.");
        }

        private void TryCallEmergencyFuel()
        {
            if (_fuel > 1f) { Notification.Show("~y~You still have fuel - no need to call for help."); return; }
            if (_outOfFuelEmergencyPending) { Notification.Show("~y~Help is already on the way."); return; }

            int cost = 80;
            if (_money < cost) { Notification.Show("~r~Not enough money for emergency delivery."); return; }
            _money -= cost;
            _outOfFuelEmergencyPending = true;
            _emergencyArrival = DateTime.Now.AddSeconds(20);
            Notification.Show($"~y~Emergency fuel called~w~ (${cost}). Arriving in ~20s~.");
        }

        // =================================================================
        // DYNAMIC PRICE - hour-of-day curve + weather/weekend, using the
        // editable base prices from the config menu.
        // =================================================================
        private static float HourMultiplier(int hour)
        {
            if (hour <= 3) return 1.00f;
            if (hour <= 5) return 1.15f;
            if (hour <= 7) return 1.30f;
            if (hour <= 10) return 1.45f;
            if (hour <= 14) return 1.60f;
            if (hour <= 16) return 1.45f;
            if (hour == 17) return 1.30f;
            if (hour == 18) return 1.15f;
            if (hour <= 21) return 1.08f;
            return 1.00f;
        }

        private float GetFuelPrice(string fuelType, Station station = null)
        {
            int hour = Function.Call<int>(Hash.GET_CLOCK_HOURS);
            float mult = HourMultiplier(hour);

            if (DateTime.Now.DayOfWeek == DayOfWeek.Saturday || DateTime.Now.DayOfWeek == DayOfWeek.Sunday) mult *= 1.03f;
            if (World.Weather == Weather.Raining || World.Weather == Weather.ThunderStorm) mult *= 1.08f;
            if (Rand.Next(1000) < 2) mult *= 1.25f;

            // city vs mountain region price band, randomized per-station at load
            if (station != null) mult *= station.RegionPriceMult;

            switch (fuelType)
            {
                case "Diesel": return _priceDiesel * mult;
                case "Premium": return _pricePremium * mult;
                case "Electric": return _priceElectric * mult;
                default: return _priceRegular * mult;
            }
        }

        private const float ENGINE_OIL_PRICE_PER_UNIT = 0.15f;
        private const float TRANS_OIL_PRICE_PER_UNIT = 0.10f;
        private float GetRepairCost(Vehicle veh) => ((1000f - veh.EngineHealth) / 1000f) * 200f;

        // =================================================================
        // PUMP PROXIMITY + SERVICE MENU
        // =================================================================
        private (Station, string, Vector3) FindNearbyMachine(Vector3 pos, float range)
        {
            foreach (var st in _stations)
                foreach (var kvp in st.Machines)
                    if (kvp.Value.DistanceTo(pos) <= range)
                        return (st, kvp.Key, kvp.Value);
            return (null, null, Vector3.Zero);
        }

        // Determines which fuel type a vehicle actually needs - matches the
        // reference mod's per-vehicle-type pump restriction (e.g. Ion pump is
        // Hybrids only).
        private string GetRequiredFuelType(Vehicle veh)
        {
            string model = veh.Model.ToString().ToLower();
            string[] electricModels = { "surge", "voltic", "raiden", "cyclone", "neon", "tezeract" };
            if (Array.Exists(electricModels, m => m == model)) return "Electric";

            if (veh.ClassType == VehicleClass.Industrial || veh.ClassType == VehicleClass.Service ||
                veh.ClassType == VehicleClass.Commercial) return "Diesel";

            if (veh.ClassType == VehicleClass.Sports || veh.ClassType == VehicleClass.Super) return "Premium";

            return "Regular";
        }

        // Shows the styled service panel automatically as soon as you're near the
        // correct machine - no separate "open" key needed, matching the reference
        // layout where the panel is just always there while you're at the pump.
        private void UpdatePumpProximity()
        {
            if (_configOpen || _activeJob != ActiveJob.None) return;
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) { _nearStation = null; _menuOpen = false; return; }
            Vehicle veh = playerPed.CurrentVehicle;
            if (veh == null || veh.Driver != playerPed || veh.Speed > 1.0f) { _nearStation = null; _menuOpen = false; return; }

            var (station, type, pos) = FindNearbyMachine(veh.Position, 6f);
            _nearStation = station;
            _nearFuelType = type;
            if (station == null) { _menuOpen = false; return; }

            if (station.OutOfStock)
            {
                Screen.ShowSubtitle($"~r~{station.Name}~w~\nOut of fuel right now - try another station.", 60);
                _menuOpen = false;
                return;
            }

            string required = GetRequiredFuelType(veh);
            if (type != required)
            {
                // matches the reference mod's per-pump restriction message
                string audience = type == "Electric" ? "Hybrids" : type == "Diesel" ? "Diesel vehicles" :
                                   type == "Premium" ? "Sports/Super cars" : "Regular vehicles";
                Screen.ShowSubtitle($"~r~{type.ToUpper()} PUMP~w~\nThis pump is for {audience} only", 60);
                _menuOpen = false;
                return;
            }

            if (!_menuOpen)
            {
                _menuOpen = true;
                _menuIndex = 0;
                _fuelRequested = Math.Min(20f, Math.Max(0f, _maxFuel - _fuel));
            }
            DrawMenu();
        }

        // Randomly takes a station out of stock for a while, and brings expired
        // ones back - checked periodically, not every tick.
        private DateTime _lastStockCheck = DateTime.MinValue;
        private void UpdateStationStock()
        {
            if ((DateTime.Now - _lastStockCheck).TotalSeconds < 30) return;
            _lastStockCheck = DateTime.Now;

            foreach (var st in _stations)
            {
                if (st.OutOfStock && DateTime.Now >= st.OutOfStockUntil)
                {
                    st.OutOfStock = false;
                }
                else if (!st.OutOfStock && Rand.Next(1000) < 3) // small chance per 30s check
                {
                    st.OutOfStock = true;
                    st.OutOfStockUntil = DateTime.Now.AddMinutes(2 + Rand.Next(6));
                }
            }
        }

        // =================================================================
        // FUEL THEFT - walk up to a parked vehicle, a panel appears on its own
        // (same idea as the pump panel), Numpad5 siphons some fuel into your
        // tank. No dedicated key, just proximity + the existing confirm key.
        // =================================================================
        private void UpdateTheftProximity()
        {
            if (_menuOpen || _configOpen || _activeJob != ActiveJob.None) { _theftMenuOpen = false; return; }
            Ped playerPed = Game.Player.Character;
            if (playerPed.IsInVehicle()) { _theftMenuOpen = false; return; }

            Vehicle nearest = null;
            float bestDist = 3.0f;
            foreach (Vehicle v in World.GetNearbyVehicles(playerPed.Position, 3.0f))
            {
                if (v == null || !v.Exists()) continue;
                if (v == playerPed.CurrentVehicle) continue;
                if (v.Driver != null && v.Driver.Exists() && !v.Driver.IsDead) continue; // don't rob a driven vehicle
                float d = v.Position.DistanceTo(playerPed.Position);
                if (d < bestDist) { bestDist = d; nearest = v; }
            }

            if (nearest == null) { _theftMenuOpen = false; _theftTarget = null; return; }

            _theftTarget = nearest;
            _theftMenuOpen = true;
            DrawTheftMenu();
        }

        private void DrawTheftMenu()
        {
            float x = 60f, y = 200f;
            new ContainerElement(new PointF(x - 15, y - 70), new SizeF(320, 120), Color.FromArgb(230, 15, 15, 15)).Draw();
            new ContainerElement(new PointF(x - 15, y - 70), new SizeF(320, 34), Color.FromArgb(255, 150, 40, 40)).Draw();
            new TextElement("SIPHON FUEL", new PointF(x, y - 65), 0.32f, Color.White, Font.ChaletLondon).Draw();
            new TextElement("[x] Siphon ~5-15L from this vehicle", new PointF(x, y - 20), 0.26f, Color.FromArgb(255, 255, 220, 220), Font.ChaletLondon).Draw();
            new TextElement("Numpad 5: confirm (risky - may draw attention)", new PointF(x, y + 10), 0.2f, Color.LightGray, Font.ChaletLondon).Draw();
        }

        private void ConfirmTheft()
        {
            if (_theftTarget == null || !_theftTarget.Exists()) { _theftMenuOpen = false; return; }

            float stolen = 5f + (float)Rand.NextDouble() * 10f;
            float space = _maxFuel - _fuel;
            stolen = Math.Min(stolen, Math.Max(0f, space));

            if (stolen <= 0.2f) { Notification.Show("~y~Your tank is already full."); return; }

            _fuel += stolen;
            Notification.Show($"~g~Siphoned {stolen:F1}L~w~ of fuel. Keep an eye out.");

            // small chance of getting noticed
            if (Rand.Next(100) < 25)
            {
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, Game.Player, 1, false);
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, Game.Player, false);
                Notification.Show("~r~Someone saw you!");
            }

            _theftMenuOpen = false;
            SaveData();
        }

        private void DrawMenu()
        {
            float x = 60f, y = 200f, rowH = 28f;
            Vehicle veh = Game.Player.Character.CurrentVehicle;

            (string label, float amount, float cost)[] items =
            {
                ($"Fuel ({_nearFuelType})", _fuelRequested, _fuelRequested * GetFuelPrice(_nearFuelType, _nearStation)),
                ("Engine oil", ENGINE_OIL_MAX - _engineOil, (ENGINE_OIL_MAX - _engineOil) * ENGINE_OIL_PRICE_PER_UNIT),
                ("Transmission oil", TRANS_OIL_MAX - _transOil, (TRANS_OIL_MAX - _transOil) * TRANS_OIL_PRICE_PER_UNIT),
                ("Repair vehicle", 0, GetRepairCost(veh)),
            };

            float panelH = 70 + items.Length * rowH + 70;
            new ContainerElement(new PointF(x - 15, y - 70), new SizeF(360, panelH), Color.FromArgb(230, 15, 15, 15)).Draw();
            new ContainerElement(new PointF(x - 15, y - 70), new SizeF(360, 34), Color.FromArgb(255, 40, 150, 60)).Draw();
            new TextElement("SAIF FUEL MOD", new PointF(x, y - 65), 0.35f, Color.White, Font.ChaletLondon).Draw();
            new ContainerElement(new PointF(x - 15, y - 34), new SizeF(360, 26), Color.FromArgb(255, 60, 180, 80)).Draw();
            new TextElement($"{_nearFuelType.ToUpper()} - {_nearStation.Name}", new PointF(x, y - 32), 0.24f, Color.White, Font.ChaletLondon).Draw();

            float rowY = y + 10;
            float total = 0f;
            for (int i = 0; i < items.Length; i++)
            {
                bool selected = i == _menuIndex;
                Color rowColor = selected ? Color.FromArgb(255, 200, 255, 200) : Color.FromArgb(230, 230, 230, 230);
                string dot = selected ? "[x]" : "[ ]";
                string amountText = i == 0 ? $"{items[i].amount:F0} L" : (items[i].cost > 0.5f ? "needed" : "full");
                new TextElement($"{dot} {items[i].label}", new PointF(x, rowY + i * rowH), 0.26f, rowColor, Font.ChaletLondon).Draw();
                new TextElement($"{amountText}   ${items[i].cost:F2}", new PointF(x + 220, rowY + i * rowH), 0.24f, Color.LightGreen, Font.ChaletLondon).Draw();
                if (selected) total = items[i].cost;
            }

            float totalY = rowY + items.Length * rowH + 15;
            new ContainerElement(new PointF(x, totalY), new SizeF(330, 30), Color.FromArgb(255, 60, 180, 80)).Draw();
            new TextElement($"Total: ${total:F2}", new PointF(x + 10, totalY + 5), 0.3f, Color.Black, Font.ChaletLondon).Draw();

            new TextElement("Numpad 8/2 move   4/6 adjust fuel   5 confirm", new PointF(x, totalY + 35), 0.2f, Color.LightGray, Font.ChaletLondon).Draw();
        }

        private void HandleMenuInput(Keys key)
        {
            int itemCount = 4;
            if (key == Keys.NumPad8) _menuIndex = (_menuIndex - 1 + itemCount) % itemCount;
            else if (key == Keys.NumPad2) _menuIndex = (_menuIndex + 1) % itemCount;
            else if (key == Keys.NumPad4 && _menuIndex == 0) _fuelRequested = Math.Max(5f, _fuelRequested - 5f);
            else if (key == Keys.NumPad6 && _menuIndex == 0) _fuelRequested = Math.Min(Math.Max(0f, _maxFuel - _fuel), _fuelRequested + 5f);
            else if (key == Keys.NumPad5) ConfirmMenuSelection();
        }

        private void ConfirmMenuSelection()
        {
            switch ((ServiceItem)_menuIndex)
            {
                case ServiceItem.Fuel:
                    if (_fuelRequested <= 0.5f) { Notification.Show("~y~Tank is already full."); return; }
                    StartJob(ActiveJob.Refuel, _fuelRequested * GetFuelPrice(_nearFuelType, _nearStation), "Refueling");
                    break;
                case ServiceItem.EngineOil:
                    float oilCost = (ENGINE_OIL_MAX - _engineOil) * ENGINE_OIL_PRICE_PER_UNIT;
                    if (oilCost <= 0.5f) { Notification.Show("~y~Engine oil is already full."); return; }
                    StartJob(ActiveJob.ChangeEngineOil, oilCost, "Changing engine oil");
                    break;
                case ServiceItem.TransOil:
                    float transCost = (TRANS_OIL_MAX - _transOil) * TRANS_OIL_PRICE_PER_UNIT;
                    if (transCost <= 0.5f) { Notification.Show("~y~Transmission oil is already full."); return; }
                    StartJob(ActiveJob.ChangeTransOil, transCost, "Changing transmission oil");
                    break;
                case ServiceItem.Repair:
                    Vehicle veh = Game.Player.Character.CurrentVehicle;
                    float repairCost = GetRepairCost(veh);
                    if (repairCost <= 0.5f) { Notification.Show("~y~Your vehicle is in great condition, no need to repair."); return; }
                    StartJob(ActiveJob.Repair, repairCost, "Repairing vehicle");
                    break;
            }
        }

        private void StartJob(ActiveJob job, float cost, string label)
        {
            if (_money < cost) { Notification.Show("~r~Your money is not enough."); return; }
            _money -= (int)Math.Round(cost);
            _activeJob = job;
            _jobTargetFuel = job == ActiveJob.Refuel ? Math.Min(_maxFuel, _fuel + _fuelRequested) : 0f;
            _menuOpen = false;
            Notification.Show($"~g~{label}...~w~ ${cost:F2}");
        }

        private void UpdateActiveJob()
        {
            if (_activeJob == ActiveJob.None) return;
            Ped playerPed = Game.Player.Character;
            Vehicle veh = playerPed.IsInVehicle() ? playerPed.CurrentVehicle : null;
            if (veh == null) { _activeJob = ActiveJob.None; return; }

            switch (_activeJob)
            {
                case ActiveJob.Refuel:
                    _fuel = Math.Min(_jobTargetFuel, _fuel + _refuelSpeed * Game.LastFrameTime);
                    if (_fuel >= _jobTargetFuel - 0.05f) { _activeJob = ActiveJob.None; Notification.Show("~g~Refuel complete!"); SaveData(); }
                    break;
                case ActiveJob.ChangeEngineOil:
                    _engineOil = Math.Min(ENGINE_OIL_MAX, _engineOil + _oilChangeSpeed * Game.LastFrameTime);
                    if (_engineOil >= ENGINE_OIL_MAX - 0.5f) { _activeJob = ActiveJob.None; Notification.Show("~g~Engine oil changed!"); SaveData(); }
                    break;
                case ActiveJob.ChangeTransOil:
                    _transOil = Math.Min(TRANS_OIL_MAX, _transOil + _oilChangeSpeed * Game.LastFrameTime);
                    if (_transOil >= TRANS_OIL_MAX - 0.5f) { _activeJob = ActiveJob.None; Notification.Show("~g~Transmission oil changed!"); SaveData(); }
                    break;
                case ActiveJob.Repair:
                    veh.EngineHealth = Math.Min(1000f, veh.EngineHealth + 150f * Game.LastFrameTime);
                    if (veh.EngineHealth >= 999.5f) { veh.Repair(); _activeJob = ActiveJob.None; Notification.Show("~g~Vehicle repaired!"); }
                    break;
            }
        }

        // =================================================================
        // RADIO TOGGLE (X)
        // =================================================================
        private void ToggleRadio()
        {
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) return;
            Vehicle veh = playerPed.CurrentVehicle;
            _radioOn = !_radioOn;
            Function.Call(Hash.SET_VEHICLE_RADIO_ENABLED, veh, _radioOn);
            Notification.Show(_radioOn ? "~g~Radio on" : "~y~Radio off");
        }

        // =================================================================
        // HUD - vertical fuel bar + speed track beside the minimap
        // =================================================================
        // Vertical segmented fuel bar docked right next to the minimap, with a
        // thin speed track beside it - a vertical line where a marker slides up
        // as speed increases, matching the reference layout (no separate boxed
        // speedometer).
        private void UpdateHud()
        {
            const int SEGMENTS = 12;
            float baseBarHeight = 220f, baseWidth = 26f;
            float barHeight = baseBarHeight * _hudScale;
            float segW = baseWidth * _hudScale;

            // work out the anchor point for the chosen corner (fuel bar sits just
            // beside the minimap when in a bottom corner, matching the reference)
            float x, barTop;
            switch (_hudCorner)
            {
                case HudCorner.BottomRight: x = Screen.Width - 60f; barTop = Screen.Height - 260f * _hudScale; break;
                case HudCorner.TopLeft: x = 285f; barTop = 90f; break;
                case HudCorner.TopRight: x = Screen.Width - 60f; barTop = 90f; break;
                default: x = 285f; barTop = Screen.Height - 260f * _hudScale; break; // BottomLeft
            }

            float fuelPct = _maxFuel > 0 ? _fuel / _maxFuel : 0f;
            float oilPct = _engineOil / ENGINE_OIL_MAX;

            if (_hudStyle == HudStyle.DigitalPercent)
            {
                new ContainerElement(new PointF(x - 6, barTop - 6), new SizeF(160 * _hudScale, 70 * _hudScale), Color.FromArgb(190, 0, 0, 0)).Draw();
                Color fuelColor = fuelPct < 0.15f ? Color.Red : (fuelPct < 0.3f ? Color.Orange : Color.LimeGreen);
                new TextElement($"FUEL {fuelPct * 100:F0}%", new PointF(x + 4, barTop), 0.3f * _hudScale, fuelColor, Font.ChaletLondon).Draw();
                Color oilColorD = oilPct < 0.3f ? Color.Red : Color.LimeGreen;
                new TextElement($"OIL {oilPct * 100:F0}%", new PointF(x + 4, barTop + 30 * _hudScale), 0.26f * _hudScale, oilColorD, Font.ChaletLondon).Draw();
            }
            else
            {
                float segGap = 3f, segHeight = (barHeight - segGap * (SEGMENTS - 1)) / SEGMENTS;
                int litSegments = (int)Math.Round(fuelPct * SEGMENTS);

                new ContainerElement(new PointF(x - 6, barTop - 22), new SizeF(segW + 12, barHeight + 30), Color.FromArgb(150, 0, 0, 0)).Draw();
                new TextElement("FUEL", new PointF(x - 2, barTop - 20), 0.22f, Color.White, Font.ChaletLondon).Draw();

                for (int i = 0; i < SEGMENTS; i++)
                {
                    bool lit = i >= (SEGMENTS - litSegments);
                    Color segColor = !lit ? Color.FromArgb(140, 40, 40, 40)
                                     : fuelPct < 0.15f ? Color.Red
                                     : fuelPct < 0.3f ? Color.Orange
                                     : Color.LimeGreen;
                    float segY = barTop + i * (segHeight + segGap);
                    new ContainerElement(new PointF(x, segY), new SizeF(segW, segHeight), segColor).Draw();
                }

                Color oilColor = oilPct < 0.3f ? Color.Red : Color.LimeGreen;
                new TextElement("OIL", new PointF(x - 2, barTop + barHeight + 8), 0.2f, Color.White, Font.ChaletLondon).Draw();
                new ContainerElement(new PointF(x, barTop + barHeight + 24), new SizeF(segW, 8), Color.FromArgb(140, 60, 60, 60)).Draw();
                new ContainerElement(new PointF(x, barTop + barHeight + 24), new SizeF(segW * oilPct, 8), oilColor).Draw();
            }

            // Speed track - a vertical line beside the fuel bar; the marker slides
            // up toward the top as speed approaches the vehicle's top speed.
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) return;
            Vehicle veh = playerPed.CurrentVehicle;
            if (veh == null || !veh.Exists()) return;

            float trackX = x + segW + 14f;
            new ContainerElement(new PointF(trackX, barTop), new SizeF(2, barHeight), Color.FromArgb(160, 220, 220, 220)).Draw();

            float maxSpeed = Function.Call<float>(Hash.GET_VEHICLE_MODEL_ESTIMATED_MAX_SPEED, veh.Model.Hash);
            float speedPct = Math.Min(1f, Math.Max(0f, veh.Speed / Math.Max(1f, maxSpeed)));
            float markerY = barTop + barHeight - (speedPct * barHeight);

            Color markerColor = speedPct < 0.7f ? Color.LimeGreen : (speedPct < 0.9f ? Color.Orange : Color.Red);
            new ContainerElement(new PointF(trackX - 6, markerY - 2), new SizeF(14, 4), markerColor).Draw();

            int kmh = (int)(veh.Speed * 3.6f);
            new TextElement($"{kmh}", new PointF(trackX - 8, barTop - 20), 0.28f, Color.White, Font.ChaletLondon).Draw();
        }

        // =================================================================
        // RUNTIME CONFIG MENU (F7)
        // =================================================================
        private void ToggleConfigMenu()
        {
            _configOpen = !_configOpen;
            _configIndex = 0;
            if (!_configOpen) { SaveConfig(); SaveHudConfig(); }
        }

        private void DrawConfigMenu()
        {
            float x = 500f, y = 100f, rowH = 26f;
            string[] labels =
            {
                $"Fuel consume rate: {_fuelConsumeRate:F2}x",
                $"Oil consume rate: {_oilConsumeRate:F2}x",
                $"Refuel speed: {_refuelSpeed:F1} L/s",
                $"Oil change speed: {_oilChangeSpeed:F0} units/s",
                $"Oil affects engine: {(_oilAffectsEngine ? "Yes" : "No")}",
                $"Regular price: ${_priceRegular:F2}/L",
                $"Diesel price: ${_priceDiesel:F2}/L",
                $"Premium price: ${_pricePremium:F2}/L",
                $"HUD style: {(_hudStyle == HudStyle.VerticalBar ? "Vertical Bar" : "Digital %")}",
                $"HUD corner: {_hudCorner}",
                $"HUD size: {_hudScale:F1}x",
                "-- close: F7 again --",
            };

            new ContainerElement(new PointF(x - 10, y - 30), new SizeF(420, rowH * labels.Length + 50), Color.FromArgb(210, 0, 0, 0)).Draw();
            new TextElement("SAIF FUEL MOD - SETTINGS", new PointF(x, y - 25), 0.3f, Color.White, Font.ChaletLondon).Draw();

            for (int i = 0; i < labels.Length; i++)
            {
                Color c = i == _configIndex ? Color.Yellow : Color.White;
                new TextElement(labels[i], new PointF(x, y + i * rowH), 0.26f, c, Font.ChaletLondon).Draw();
            }
            new TextElement("W/S: move   A/D: adjust   F7: save & close", new PointF(x, y + labels.Length * rowH + 15), 0.22f, Color.LightGray, Font.ChaletLondon).Draw();
        }

        private void HandleConfigInput(Keys key)
        {
            if (key == Keys.W) _configIndex = (_configIndex - 1 + CONFIG_ITEM_COUNT) % CONFIG_ITEM_COUNT;
            else if (key == Keys.S) _configIndex = (_configIndex + 1) % CONFIG_ITEM_COUNT;
            else if (key == Keys.A) AdjustConfig(-1);
            else if (key == Keys.D) AdjustConfig(1);
        }

        private void AdjustConfig(int dir)
        {
            switch (_configIndex)
            {
                case 0: _fuelConsumeRate = Clamp(_fuelConsumeRate + dir * 0.05f, 0.2f, 3.0f); break;
                case 1: _oilConsumeRate = Clamp(_oilConsumeRate + dir * 0.05f, 0.2f, 3.0f); break;
                case 2: _refuelSpeed = Clamp(_refuelSpeed + dir * 0.5f, 1.0f, 20.0f); break;
                case 3: _oilChangeSpeed = Clamp(_oilChangeSpeed + dir * 5f, 10f, 100f); break;
                case 4: if (dir != 0) _oilAffectsEngine = !_oilAffectsEngine; break;
                case 5: _priceRegular = Clamp(_priceRegular + dir * 0.05f, 0.5f, 15f); break;
                case 6: _priceDiesel = Clamp(_priceDiesel + dir * 0.05f, 0.5f, 15f); break;
                case 7: _pricePremium = Clamp(_pricePremium + dir * 0.05f, 0.5f, 15f); break;
                case 8: if (dir != 0) _hudStyle = _hudStyle == HudStyle.VerticalBar ? HudStyle.DigitalPercent : HudStyle.VerticalBar; break;
                case 9:
                    int corner = ((int)_hudCorner + dir + 4) % 4;
                    _hudCorner = (HudCorner)corner;
                    break;
                case 10: _hudScale = Clamp(_hudScale + dir * 0.1f, 0.6f, 1.6f); break;
            }
        }

        private static float Clamp(float v, float min, float max) => Math.Max(min, Math.Min(max, v));

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                var lines = File.ReadAllLines(ConfigPath);
                if (lines.Length >= 8)
                {
                    _fuelConsumeRate = float.Parse(lines[0]);
                    _oilConsumeRate = float.Parse(lines[1]);
                    _refuelSpeed = float.Parse(lines[2]);
                    _oilChangeSpeed = float.Parse(lines[3]);
                    _oilAffectsEngine = lines[4] == "true";
                    _priceRegular = float.Parse(lines[5]);
                    _priceDiesel = float.Parse(lines[6]);
                    _pricePremium = float.Parse(lines[7]);
                }
            }
            catch (Exception ex) { Log("Config load error: " + ex.Message); }
        }

        private void SaveConfig()
        {
            try
            {
                var lines = new[]
                {
                    _fuelConsumeRate.ToString("F2"), _oilConsumeRate.ToString("F2"),
                    _refuelSpeed.ToString("F1"), _oilChangeSpeed.ToString("F0"),
                    _oilAffectsEngine.ToString().ToLower(),
                    _priceRegular.ToString("F2"), _priceDiesel.ToString("F2"), _pricePremium.ToString("F2")
                };
                File.WriteAllLines(ConfigPath, lines);
                Notification.Show("~g~Settings saved!");
            }
            catch (Exception ex) { Log("Config save error: " + ex.Message); }
        }

        private void LoadHudConfig()
        {
            try
            {
                if (!File.Exists(HudConfigPath)) return;
                var lines = File.ReadAllLines(HudConfigPath);
                if (lines.Length >= 3)
                {
                    _hudStyle = lines[0] == "1" ? HudStyle.DigitalPercent : HudStyle.VerticalBar;
                    _hudCorner = (HudCorner)int.Parse(lines[1]);
                    _hudScale = float.Parse(lines[2]);
                }
            }
            catch (Exception ex) { Log("HUD config load error: " + ex.Message); }
        }

        private void SaveHudConfig()
        {
            try
            {
                var lines = new[] { _hudStyle == HudStyle.DigitalPercent ? "1" : "0", ((int)_hudCorner).ToString(), _hudScale.ToString("F1") };
                File.WriteAllLines(HudConfigPath, lines);
            }
            catch (Exception ex) { Log("HUD config save error: " + ex.Message); }
        }

        // =================================================================
        // AMBIENT VEHICLES - 2-3 always refuelling
        // =================================================================
        private void UpdateAmbientVehicles()
        {
            if ((DateTime.Now - _lastAmbientCheck).TotalSeconds < 5) return;
            _lastAmbientCheck = DateTime.Now;

            for (int i = _ambientVehicles.Count - 1; i >= 0; i--)
            {
                if (_ambientVehicles[i] == null || !_ambientVehicles[i].Exists())
                {
                    _ambientVehicles.RemoveAt(i);
                    if (i < _ambientPeds.Count) _ambientPeds.RemoveAt(i);
                }
            }

            if (_ambientVehicles.Count >= AMBIENT_TARGET_COUNT || _stations.Count == 0) return;

            var station = _stations[Rand.Next(_stations.Count)];
            var machine = station.Machines.Values.First();
            Vector3 spawnPos = machine + new Vector3(3f, 0f, 0f);
            if (spawnPos.DistanceTo(Game.Player.Character.Position) < 40f) return;

            VehicleHash[] models = { VehicleHash.Sentinel, VehicleHash.Taxi, VehicleHash.Dominator, VehicleHash.Baller };
            Vehicle veh = World.CreateVehicle(models[Rand.Next(models.Length)], spawnPos);
            if (veh == null) return;
            veh.IsPersistent = true;

            Ped driver = World.CreatePed(PedHash.Business02AMY, spawnPos);
            if (driver != null)
            {
                driver.IsPersistent = true;
                driver.SetIntoVehicle(veh, VehicleSeat.Driver);
                driver.Task.LeaveVehicle();
                _ambientPeds.Add(driver);
            }
            _ambientVehicles.Add(veh);
        }

        // =================================================================
        // PERSISTENCE - fuel/oil saved per vehicle plate, restored (not random)
        // =================================================================
        private void LoadSaveData()
        {
            try
            {
                if (!File.Exists(SavePath)) return;
                foreach (var line in File.ReadAllLines(SavePath))
                {
                    var parts = line.Split('|');
                    if (parts.Length == 4 &&
                        float.TryParse(parts[1], out float f) &&
                        float.TryParse(parts[2], out float eo) &&
                        float.TryParse(parts[3], out float to))
                    {
                        _savedByPlate[parts[0]] = new[] { f, eo, to };
                    }
                }
            }
            catch (Exception ex) { Log("Load error: " + ex.Message); }
        }

        private void SaveData()
        {
            try
            {
                Ped playerPed = Game.Player.Character;
                if (playerPed.IsInVehicle())
                {
                    string plate = Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, playerPed.CurrentVehicle);
                    if (!string.IsNullOrEmpty(plate))
                        _savedByPlate[plate] = new[] { _fuel, _engineOil, _transOil };
                }

                var lines = _savedByPlate.Select(kv => $"{kv.Key}|{kv.Value[0]:F1}|{kv.Value[1]:F1}|{kv.Value[2]:F1}");
                File.WriteAllLines(SavePath, lines);
            }
            catch (Exception ex) { Log("Save error: " + ex.Message); }
        }

        private void CheckVehicleSwitch()
        {
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) { _lastVehicleHandle = -1; return; }

            Vehicle veh = playerPed.CurrentVehicle;
            if (veh.Handle == _lastVehicleHandle) return;
            _lastVehicleHandle = veh.Handle;

            string plate = Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, veh);
            _maxFuel = GetTankSize(veh);

            if (!string.IsNullOrEmpty(plate) && _savedByPlate.TryGetValue(plate, out float[] saved))
            {
                _fuel = saved[0];
                _engineOil = saved[1];
                _transOil = saved[2];
            }
            else
            {
                _fuel = _maxFuel * 0.5f; // new/unknown vehicle starts half full, not random
                _engineOil = ENGINE_OIL_MAX;
                _transOil = TRANS_OIL_MAX;
            }

            _radioOn = true;
        }

        // =================================================================
        // KEY HANDLING
        // =================================================================
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.KeyCode == Keys.F7)
                {
                    ToggleConfigMenu();
                    return;
                }

                if (_configOpen) { HandleConfigInput(e.KeyCode); return; }

                if (_menuOpen) { HandleMenuInput(e.KeyCode); return; }

                if (_theftMenuOpen) { if (e.KeyCode == Keys.NumPad5) ConfirmTheft(); return; }

                if (e.KeyCode == Keys.X) ToggleRadio();
                else if (e.KeyCode == Keys.H) TryCallEmergencyFuel();
            }
            catch (Exception ex) { Log("OnKeyDown error: " + ex.Message); }
        }

        private static void Log(string msg)
        {
            try { File.AppendAllText("scripts\\SaifFuelMod.log", $"[{DateTime.Now:HH:mm:ss}] {msg}\r\n"); }
            catch { /* ignore */ }
        }
    }
}
