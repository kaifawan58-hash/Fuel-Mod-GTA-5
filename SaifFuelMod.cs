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
//  - Ctrl+Shift+C opens a runtime config menu (rates, prices) with the same
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
        }

        private readonly List<Station> _stations = new List<Station>();

        private void BuildStations()
        {
            void Add(string name, params (string type, float x, float y, float z)[] machines)
            {
                var s = new Station { Name = name };
                foreach (var m in machines) s.Machines[m.type] = new Vector3(m.x, m.y, m.z);
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
        // RUNTIME CONFIG (editable in-game via Ctrl+Shift+C, mirrors the
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
        private class RememberedVehicle { public int ModelHash; public string Plate; public float Fuel; }
        private readonly RememberedVehicle[] _remembered = new RememberedVehicle[4];
        private int _rememberCursor = 0;

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
        // CONFIG MENU STATE (Ctrl+Shift+C opens, same W/S/A/D/E/Q controls)
        // =================================================================
        private bool _configOpen = false;
        private int _configIndex = 0;
        private const int CONFIG_ITEM_COUNT = 8;

        private static readonly Random Rand = new Random();

        private readonly List<Vehicle> _ambientVehicles = new List<Vehicle>();
        private readonly List<Ped> _ambientPeds = new List<Ped>();
        private const int AMBIENT_TARGET_COUNT = 3;
        private DateTime _lastAmbientCheck = DateTime.MinValue;

        private bool _stationsSpawned = false;

        public FuelMod()
        {
            BuildStations();
            LoadSaveData();
            LoadConfig();

            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += (s, e) => SaveData();

            Notification.Show("~g~Saif Fuel Mod~w~ loaded.");
            Notification.Show("~y~Approach a pump, press ~b~E~y~ for the service menu.");
            Notification.Show("~y~X~w~=radio  ~y~F~w~=remember vehicle fuel  ~y~H~w~=emergency fuel  ~y~Ctrl+Shift+C~w~=settings");
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
                UpdateSpeedometer();
                UpdateHud();
                UpdatePumpProximity();
                UpdateActiveJob();
                UpdateEmergencyFuel();
                UpdateAmbientVehicles();

                if (_menuOpen) DrawMenu();
                if (_configOpen) DrawConfigMenu();
            }
            catch (Exception ex)
            {
                Log("OnTick error: " + ex.Message);
            }
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

            _fuel = _maxFuel * 0.5f;
            _outOfFuelEmergencyPending = false;
            Ped playerPed = Game.Player.Character;
            if (playerPed.IsInVehicle()) playerPed.CurrentVehicle.IsEngineRunning = false; // still needs restarting manually
            Notification.Show("~g~Emergency fuel delivered!~w~ Half a tank added.");
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

        private float GetFuelPrice(string fuelType)
        {
            int hour = Function.Call<int>(Hash.GET_CLOCK_HOURS);
            float mult = HourMultiplier(hour);

            if (DateTime.Now.DayOfWeek == DayOfWeek.Saturday || DateTime.Now.DayOfWeek == DayOfWeek.Sunday) mult *= 1.03f;
            if (World.Weather == Weather.Raining || World.Weather == Weather.ThunderStorm) mult *= 1.08f;
            if (Rand.Next(1000) < 2) mult *= 1.25f;

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

        private void UpdatePumpProximity()
        {
            if (_menuOpen || _configOpen || _activeJob != ActiveJob.None) return;
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) { _nearStation = null; return; }
            Vehicle veh = playerPed.CurrentVehicle;
            if (veh == null || veh.Driver != playerPed || veh.Speed > 1.0f) { _nearStation = null; return; }

            var (station, type, pos) = FindNearbyMachine(veh.Position, 6f);
            _nearStation = station;
            _nearFuelType = type;
            if (station == null) return;

            Screen.ShowHelpText(
                $"Press ~INPUT_CONTEXT~ for the service menu at {station.Name}\n{type}: ~g~${GetFuelPrice(type):F2}~w~/L",
                100, false, false);
        }

        private void OpenMenu()
        {
            if (_nearStation == null) { Notification.Show("~r~Not near a fuel pump!"); return; }
            _menuOpen = true;
            _menuIndex = 0;
            _fuelRequested = Math.Min(20f, Math.Max(0f, _maxFuel - _fuel));
        }

        private void DrawMenu()
        {
            float x = 60f, y = 200f, rowH = 26f;
            Vehicle veh = Game.Player.Character.CurrentVehicle;
            string[] labels =
            {
                $"Fuel ({_nearFuelType}): {_fuelRequested:F0} L  ~  ${_fuelRequested * GetFuelPrice(_nearFuelType):F2}",
                $"Engine Oil: {ENGINE_OIL_MAX - _engineOil:F0} units  ~  ${(ENGINE_OIL_MAX - _engineOil) * ENGINE_OIL_PRICE_PER_UNIT:F2}",
                $"Transmission Oil: {TRANS_OIL_MAX - _transOil:F0} units  ~  ${(TRANS_OIL_MAX - _transOil) * TRANS_OIL_PRICE_PER_UNIT:F2}",
                $"Repair Vehicle  ~  ${GetRepairCost(veh):F2}",
                "Close (Q)"
            };

            new ContainerElement(new PointF(x - 10, y - 30), new SizeF(480, rowH * labels.Length + 40), Color.FromArgb(210, 0, 0, 0)).Draw();
            new TextElement($"SAIF FUEL MOD - {_nearStation.Name}", new PointF(x, y - 25), 0.3f, Color.White, Font.ChaletLondon).Draw();

            for (int i = 0; i < labels.Length; i++)
            {
                Color c = i == _menuIndex ? Color.Yellow : Color.White;
                new TextElement(labels[i], new PointF(x, y + i * rowH), 0.28f, c, Font.ChaletLondon).Draw();
            }
            new TextElement("W/S: move   A/D: adjust fuel   E: confirm   Q: close", new PointF(x, y + labels.Length * rowH + 10), 0.24f, Color.LightGray, Font.ChaletLondon).Draw();
        }

        private void HandleMenuInput(Keys key)
        {
            int itemCount = 5;
            if (key == Keys.W) _menuIndex = (_menuIndex - 1 + itemCount) % itemCount;
            else if (key == Keys.S) _menuIndex = (_menuIndex + 1) % itemCount;
            else if (key == Keys.A && _menuIndex == 0) _fuelRequested = Math.Max(5f, _fuelRequested - 5f);
            else if (key == Keys.D && _menuIndex == 0) _fuelRequested = Math.Min(Math.Max(0f, _maxFuel - _fuel), _fuelRequested + 5f);
            else if (key == Keys.Q) _menuOpen = false;
            else if (key == Keys.E) ConfirmMenuSelection();
        }

        private void ConfirmMenuSelection()
        {
            switch ((ServiceItem)_menuIndex)
            {
                case ServiceItem.Fuel:
                    if (_fuelRequested <= 0.5f) { Notification.Show("~y~Tank is already full."); return; }
                    StartJob(ActiveJob.Refuel, _fuelRequested * GetFuelPrice(_nearFuelType), "Refueling");
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
                case ServiceItem.Close:
                    _menuOpen = false;
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
        // 4-SLOT REMEMBERED VEHICLE FUEL (F key)
        // =================================================================
        private void RememberCurrentVehicle()
        {
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) { Notification.Show("~r~Get in a vehicle first!"); return; }
            Vehicle veh = playerPed.CurrentVehicle;
            string plate = Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, veh);

            _remembered[_rememberCursor] = new RememberedVehicle
            {
                ModelHash = veh.Model.Hash,
                Plate = plate,
                Fuel = _fuel
            };
            Notification.Show($"~g~Remembered fuel for this vehicle~w~ (slot {_rememberCursor + 1}/4)");
            _rememberCursor = (_rememberCursor + 1) % 4;
        }

        private bool TryRecallRememberedFuel(Vehicle veh, string plate, out float fuel)
        {
            foreach (var r in _remembered)
            {
                if (r != null && r.ModelHash == veh.Model.Hash && r.Plate == plate)
                {
                    fuel = r.Fuel;
                    return true;
                }
            }
            fuel = 0f;
            return false;
        }

        // =================================================================
        // SPEEDOMETER + HUD
        // =================================================================
        private void UpdateSpeedometer()
        {
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) return;
            Vehicle veh = playerPed.CurrentVehicle;
            if (veh == null || !veh.Exists()) return;

            float x = 40f, y = Screen.Height - 160f;
            new ContainerElement(new PointF(x, y), new SizeF(180, 110), Color.FromArgb(190, 0, 0, 0)).Draw();

            int kmh = (int)(veh.Speed * 3.6f);
            new TextElement($"{kmh}", new PointF(x + 20, y + 5), 1.0f, Color.White, Font.ChaletLondon).Draw();
            new TextElement("km/h", new PointF(x + 20, y + 60), 0.3f, Color.FromArgb(220, 200, 200, 200), Font.ChaletLondon).Draw();

            float maxSpeed = Function.Call<float>(Hash.GET_VEHICLE_MODEL_ESTIMATED_MAX_SPEED, veh.Model.Hash);
            float pct = Math.Min(1f, Math.Max(0f, veh.Speed / Math.Max(1f, maxSpeed)));

            new ContainerElement(new PointF(x + 20, y + 85), new SizeF(110, 12), Color.FromArgb(120, 80, 80, 80)).Draw();
            Color barColor = pct < 0.7f ? Color.LimeGreen : (pct < 0.9f ? Color.Orange : Color.Red);
            new ContainerElement(new PointF(x + 22, y + 87), new SizeF(106 * pct, 8), barColor).Draw();
        }

        private void UpdateHud()
        {
            float x = Screen.Width - 260f, y = Screen.Height - 110f;
            new ContainerElement(new PointF(x, y), new SizeF(220, 90), Color.FromArgb(190, 0, 0, 0)).Draw();

            float fuelPct = _maxFuel > 0 ? _fuel / _maxFuel : 0f;
            Color fuelColor = fuelPct < 0.15f ? Color.Red : (fuelPct < 0.3f ? Color.Orange : Color.LimeGreen);
            new ContainerElement(new PointF(x + 10, y + 25), new SizeF(200, 14), Color.FromArgb(100, 60, 60, 60)).Draw();
            new ContainerElement(new PointF(x + 10, y + 25), new SizeF(200 * fuelPct, 14), fuelColor).Draw();
            new TextElement($"FUEL {fuelPct * 100:F0}%", new PointF(x + 10, y + 5), 0.28f, Color.White, Font.ChaletLondon).Draw();

            float oilPct = _engineOil / ENGINE_OIL_MAX;
            Color oilColor = oilPct < 0.3f ? Color.Red : Color.LimeGreen;
            new ContainerElement(new PointF(x + 10, y + 55), new SizeF(200, 10), Color.FromArgb(100, 60, 60, 60)).Draw();
            new ContainerElement(new PointF(x + 10, y + 55), new SizeF(200 * oilPct, 10), oilColor).Draw();
            new TextElement("OIL", new PointF(x + 10, y + 40), 0.22f, Color.White, Font.ChaletLondon).Draw();
        }

        // =================================================================
        // RUNTIME CONFIG MENU (Ctrl+Shift+C)
        // =================================================================
        private void ToggleConfigMenu()
        {
            _configOpen = !_configOpen;
            _configIndex = 0;
            if (!_configOpen) SaveConfig();
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
            };

            new ContainerElement(new PointF(x - 10, y - 30), new SizeF(420, rowH * labels.Length + 50), Color.FromArgb(210, 0, 0, 0)).Draw();
            new TextElement("SAIF FUEL MOD - SETTINGS", new PointF(x, y - 25), 0.3f, Color.White, Font.ChaletLondon).Draw();

            for (int i = 0; i < labels.Length; i++)
            {
                Color c = i == _configIndex ? Color.Yellow : Color.White;
                new TextElement(labels[i], new PointF(x, y + i * rowH), 0.26f, c, Font.ChaletLondon).Draw();
            }
            new TextElement("W/S: move   A/D: adjust   Ctrl+Shift+C: save & close", new PointF(x, y + labels.Length * rowH + 15), 0.22f, Color.LightGray, Font.ChaletLondon).Draw();
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

            if (TryRecallRememberedFuel(veh, plate, out float rememberedFuel))
            {
                _fuel = rememberedFuel;
            }
            else if (!string.IsNullOrEmpty(plate) && _savedByPlate.TryGetValue(plate, out float[] saved))
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
                if (_configOpen)
                {
                    if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey) return;
                    HandleConfigInput(e.KeyCode);
                    return;
                }
                if (e.KeyCode == Keys.C && System.Windows.Forms.Control.ModifierKeys == (Keys.Control | Keys.Shift))
                {
                    ToggleConfigMenu();
                    return;
                }

                if (_menuOpen) { HandleMenuInput(e.KeyCode); return; }

                if (e.KeyCode == Keys.E && _activeJob == ActiveJob.None) OpenMenu();
                else if (e.KeyCode == Keys.X) ToggleRadio();
                else if (e.KeyCode == Keys.F) RememberCurrentVehicle();
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
