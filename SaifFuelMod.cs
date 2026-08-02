// SaifFuelMod.cs
// C# / ScriptHookVDotNet3 port of the original Lua fuel mod, rebuilt with
// LemonUI for native-look menus (colored header, highlighted selection,
// "X / Y" counter - navigated with arrow keys + Enter/Backspace, same as
// GTA's own interaction menu).
//
// Systems:
//  - Approach the correct pump -> a LemonUI menu opens on its own: Fuel,
//    Engine Oil, Transmission Oil, Repair Vehicle, Use Jerry Can.
//  - F7 opens Settings: consumption/refuel rates, prices, HUD position/scale,
//    Siphon Nearby Vehicle, Call Fuel Delivery.
//  - Fuel delivery: call it from the Settings menu, a plane flies in (tracked
//    with a blip + on-screen distance). For land vehicles it drops a jerry
//    can near you to walk over and collect; for boats/aircraft it flies
//    alongside and tops your tank up directly with a fill progress bar.
//  - Siphoning: walk up to a parked vehicle, choose "Siphon Nearby Vehicle"
//    from Settings - a timed progress bar fills your jerry can, not the
//    vehicle directly. Use the jerry can afterwards from the pump menu (or
//    anywhere - jerry can use doesn't require a pump).
//  - Real per-machine station coordinates, dynamic hour/weekend/weather/
//    region pricing, random pump stockouts, per-vehicle fuel/oil that
//    persists automatically, ambient refuelling traffic.

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
using LemonUI;
using LemonUI.Menus;
using Font = GTA.UI.Font;
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
            public bool IsMountain;
            public float RegionPriceMult = 1f;
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

                float avgZ = 0f;
                foreach (var mv in s.Machines.Values) avgZ += mv.Z;
                avgZ /= Math.Max(1, s.Machines.Count);
                s.IsMountain = avgZ > 90f;
                s.RegionPriceMult = s.IsMountain
                    ? 1.10f + (float)Rand.NextDouble() * 0.20f
                    : 0.95f + (float)Rand.NextDouble() * 0.15f;

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
        // RUNTIME CONFIG (editable in-game via F7 settings menu)
        // =================================================================
        private float _fuelConsumeRate = 1.0f;
        private float _oilConsumeRate = 1.0f;
        private float _refuelSpeed = 4.0f;
        private float _oilChangeSpeed = 40.0f;
        private bool _oilAffectsEngine = true;
        private float _priceRegular = 1.50f;
        private float _priceDiesel = 1.42f;
        private float _pricePremium = 1.65f;
        private float _priceElectric = 0.45f;
        private const string ConfigPath = "scripts\\SaifFuelMod_config.txt";

        // HUD placement/scale - fully custom position, no preset corners
        private float _hudOffsetX = 285f;
        private float _hudOffsetY = -260f; // negative = measured up from bottom of screen
        private float _hudScale = 1.0f;
        private const string HudConfigPath = "scripts\\SaifFuelMod_hud.txt";
        private float _displayedFuelPct = 1f; // lerped toward the real value for a live-draining look

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
        private readonly Dictionary<string, float[]> _savedByPlate = new Dictionary<string, float[]>();
        private int _lastVehicleHandle = -1;
        private bool _radioOn = true;

        // Jerry can - siphoned and delivered fuel lands here first, then you
        // pour it into your vehicle from the pump menu.
        private float _jerryCanFuel = 0f;
        private const float JERRY_CAN_CAPACITY = 20f;

        private int _money = 5000;

        private static readonly Random Rand = new Random();

        private DateTime _slowConsumptionUntil = DateTime.MinValue;
        private bool _stationsSpawned = false;
        private DateTime _lastStockCheck = DateTime.MinValue;

        // Pump proximity
        private Station _nearStation = null;
        private string _nearFuelType = null;

        private enum ActiveJob { None, Refuel, ChangeEngineOil, ChangeTransOil, Repair }
        private ActiveJob _activeJob = ActiveJob.None;
        private float _jobTargetFuel = 0f;

        // Siphoning - progress bar, fills the jerry can, not the vehicle
        private bool _theftInProgress = false;
        private float _theftProgress = 0f;
        private Vehicle _theftTarget = null;
        private const float THEFT_DURATION = 6f;

        // Fuel delivery by plane
        private enum DeliveryPhase { None, Inbound, Hovering, WaitingPickup, Leaving }
        private DeliveryPhase _deliveryPhase = DeliveryPhase.None;
        private Vehicle _deliveryPlane = null;
        private Ped _deliveryPilot = null;
        private Prop _deliveryCan = null;
        private Blip _deliveryBlip = null;
        private float _deliveryFillProgress = 0f;
        private DateTime _deliveryLeaveAt = DateTime.MinValue;
        private const int DELIVERY_COST = 120;

        private readonly List<Vehicle> _ambientVehicles = new List<Vehicle>();
        private readonly List<Ped> _ambientPeds = new List<Ped>();
        private const int AMBIENT_TARGET_COUNT = 3;
        private DateTime _lastAmbientCheck = DateTime.MinValue;

        // =================================================================
        // LEMONUI MENUS
        // =================================================================
        private ObjectPool _pool;
        private NativeMenu _serviceMenu;
        private NativeListItem<int> _fuelAmountItem;
        private NativeItem _engineOilItem;
        private NativeItem _transOilItem;
        private NativeItem _repairItem;
        private NativeItem _useJerryCanItem;

        private NativeMenu _settingsMenu;
        private NativeListItem<float> _fuelRateItem;
        private NativeListItem<float> _oilRateItem;
        private NativeListItem<float> _refuelSpeedItem;
        private NativeListItem<float> _oilChangeSpeedItem;
        private NativeCheckboxItem _oilAffectsEngineItem;
        private NativeListItem<float> _priceRegularItem;
        private NativeListItem<float> _priceDieselItem;
        private NativeListItem<float> _pricePremiumItem;
        private NativeListItem<int> _hudOffsetXItem;
        private NativeListItem<int> _hudOffsetYItem;
        private NativeListItem<float> _hudScaleItem;
        private NativeItem _siphonItem;
        private NativeItem _callDeliveryItem;

        public FuelMod()
        {
            BuildStations();
            LoadSaveData();
            LoadConfig();
            LoadHudConfig();
            BuildMenus();

            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += (s, e) => { SaveData(); CleanupDelivery(); };

            Notification.Show("~g~Saif Fuel Mod~w~ loaded.");
            Notification.Show("~y~Approach a pump~w~ - the menu opens on its own. ~b~F7~y~ = settings.");
        }

        // =================================================================
        // MENU CONSTRUCTION
        // =================================================================
        private static float[] FloatSteps(float min, float max, float step)
        {
            var list = new List<float>();
            for (float v = min; v <= max + 0.0001f; v += step) list.Add((float)Math.Round(v, 2));
            return list.ToArray();
        }

        private static int[] IntSteps(int min, int max, int step)
        {
            var list = new List<int>();
            for (int v = min; v <= max; v += step) list.Add(v);
            return list.ToArray();
        }

        private static void SelectClosest(NativeListItem<float> item, float target)
        {
            int bestIdx = 0; float bestDiff = float.MaxValue;
            for (int i = 0; i < item.Items.Count; i++)
            {
                float diff = Math.Abs(item.Items[i] - target);
                if (diff < bestDiff) { bestDiff = diff; bestIdx = i; }
            }
            item.SelectedIndex = bestIdx;
        }

        private static void SelectClosest(NativeListItem<int> item, int target)
        {
            int bestIdx = 0, bestDiff = int.MaxValue;
            for (int i = 0; i < item.Items.Count; i++)
            {
                int diff = Math.Abs(item.Items[i] - target);
                if (diff < bestDiff) { bestDiff = diff; bestIdx = i; }
            }
            item.SelectedIndex = bestIdx;
        }

        private void BuildMenus()
        {
            _pool = new ObjectPool();

            // ---- Service menu (auto-shown near the right pump) ----
            _serviceMenu = new NativeMenu("Saif Fuel Mod", "Fuel Station", "");
            _fuelAmountItem = new NativeListItem<int>("Fuel", IntSteps(5, 100, 5));
            _engineOilItem = new NativeItem("Engine Oil");
            _transOilItem = new NativeItem("Transmission Oil");
            _repairItem = new NativeItem("Repair Vehicle");
            _useJerryCanItem = new NativeItem("Use Jerry Can");

            _serviceMenu.Add(_fuelAmountItem);
            _serviceMenu.Add(_engineOilItem);
            _serviceMenu.Add(_transOilItem);
            _serviceMenu.Add(_repairItem);
            _serviceMenu.Add(_useJerryCanItem);
            _serviceMenu.ItemActivated += ServiceMenu_ItemActivated;
            _pool.Add(_serviceMenu);

            // ---- Settings menu (F7) ----
            _settingsMenu = new NativeMenu("Saif Fuel Mod", "Settings", "");

            _fuelRateItem = new NativeListItem<float>("Fuel Consume Rate", FloatSteps(0.2f, 3.0f, 0.1f));
            SelectClosest(_fuelRateItem, _fuelConsumeRate);
            _fuelRateItem.ItemChanged += (s, e) => _fuelConsumeRate = _fuelRateItem.SelectedItem;

            _oilRateItem = new NativeListItem<float>("Oil Consume Rate", FloatSteps(0.2f, 3.0f, 0.1f));
            SelectClosest(_oilRateItem, _oilConsumeRate);
            _oilRateItem.ItemChanged += (s, e) => _oilConsumeRate = _oilRateItem.SelectedItem;

            _refuelSpeedItem = new NativeListItem<float>("Refuel Speed (L/s)", FloatSteps(1.0f, 20.0f, 0.5f));
            SelectClosest(_refuelSpeedItem, _refuelSpeed);
            _refuelSpeedItem.ItemChanged += (s, e) => _refuelSpeed = _refuelSpeedItem.SelectedItem;

            _oilChangeSpeedItem = new NativeListItem<float>("Oil Change Speed", FloatSteps(10f, 100f, 5f));
            SelectClosest(_oilChangeSpeedItem, _oilChangeSpeed);
            _oilChangeSpeedItem.ItemChanged += (s, e) => _oilChangeSpeed = _oilChangeSpeedItem.SelectedItem;

            _oilAffectsEngineItem = new NativeCheckboxItem("Oil Affects Engine", _oilAffectsEngine);
            _oilAffectsEngineItem.CheckboxChanged += (s, e) => _oilAffectsEngine = _oilAffectsEngineItem.Checked;

            _priceRegularItem = new NativeListItem<float>("Regular Price", FloatSteps(0.5f, 15f, 0.1f));
            SelectClosest(_priceRegularItem, _priceRegular);
            _priceRegularItem.ItemChanged += (s, e) => _priceRegular = _priceRegularItem.SelectedItem;

            _priceDieselItem = new NativeListItem<float>("Diesel Price", FloatSteps(0.5f, 15f, 0.1f));
            SelectClosest(_priceDieselItem, _priceDiesel);
            _priceDieselItem.ItemChanged += (s, e) => _priceDiesel = _priceDieselItem.SelectedItem;

            _pricePremiumItem = new NativeListItem<float>("Premium Price", FloatSteps(0.5f, 15f, 0.1f));
            SelectClosest(_pricePremiumItem, _pricePremium);
            _pricePremiumItem.ItemChanged += (s, e) => _pricePremium = _pricePremiumItem.SelectedItem;

            _hudOffsetXItem = new NativeListItem<int>("HUD X Position", IntSteps(0, 1800, 10));
            SelectClosest(_hudOffsetXItem, (int)_hudOffsetX);
            _hudOffsetXItem.ItemChanged += (s, e) => _hudOffsetX = _hudOffsetXItem.SelectedItem;

            _hudOffsetYItem = new NativeListItem<int>("HUD Y Position (from bottom)", IntSteps(-800, -50, 10));
            SelectClosest(_hudOffsetYItem, (int)_hudOffsetY);
            _hudOffsetYItem.ItemChanged += (s, e) => _hudOffsetY = _hudOffsetYItem.SelectedItem;

            _hudScaleItem = new NativeListItem<float>("HUD Size", FloatSteps(0.6f, 1.6f, 0.1f));
            SelectClosest(_hudScaleItem, _hudScale);
            _hudScaleItem.ItemChanged += (s, e) => _hudScale = _hudScaleItem.SelectedItem;

            _siphonItem = new NativeItem("Siphon Nearby Vehicle", "Fills your jerry can, not your tank directly.");
            _callDeliveryItem = new NativeItem("Call Fuel Delivery", $"${DELIVERY_COST} - a plane brings fuel to you.");

            _settingsMenu.Add(_fuelRateItem);
            _settingsMenu.Add(_oilRateItem);
            _settingsMenu.Add(_refuelSpeedItem);
            _settingsMenu.Add(_oilChangeSpeedItem);
            _settingsMenu.Add(_oilAffectsEngineItem);
            _settingsMenu.Add(_priceRegularItem);
            _settingsMenu.Add(_priceDieselItem);
            _settingsMenu.Add(_pricePremiumItem);
            _settingsMenu.Add(_hudOffsetXItem);
            _settingsMenu.Add(_hudOffsetYItem);
            _settingsMenu.Add(_hudScaleItem);
            _settingsMenu.Add(_siphonItem);
            _settingsMenu.Add(_callDeliveryItem);
            _settingsMenu.ItemActivated += SettingsMenu_ItemActivated;
            _pool.Add(_settingsMenu);
        }

        private void ServiceMenu_ItemActivated(object sender, ItemActivatedArgs e)
        {
            if (e.Item == _fuelAmountItem) ConfirmRefuel(_fuelAmountItem.SelectedItem);
            else if (e.Item == _engineOilItem) ConfirmEngineOil();
            else if (e.Item == _transOilItem) ConfirmTransOil();
            else if (e.Item == _repairItem) ConfirmRepair();
            else if (e.Item == _useJerryCanItem) UseJerryCan();
        }

        private void SettingsMenu_ItemActivated(object sender, ItemActivatedArgs e)
        {
            if (e.Item == _siphonItem) TryStartSiphon();
            else if (e.Item == _callDeliveryItem) TryCallDelivery();
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
                    // is illegal before the script's main loop starts, so spawning is
                    // deferred to the first Tick instead of the constructor.
                    foreach (var st in _stations) CreateStationBlips(st);
                    _stationsSpawned = true;
                }

                _pool.Process();

                CheckVehicleSwitch();
                UpdateFuelAndOilDrain();
                AutoRememberFuel();
                UpdateHud();
                UpdateStationStock();
                UpdatePumpProximity();
                UpdateActiveJob();
                UpdateSiphonProgress();
                UpdateDelivery();
                UpdateAmbientVehicles();
            }
            catch (Exception ex)
            {
                Log("OnTick error: " + ex.Message);
            }
        }

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
        // TOP-LEFT WARNING TEXT (replaces center-screen subtitles)
        // =================================================================
        private string _warningText = null;
        private DateTime _warningUntil = DateTime.MinValue;
        private void ShowWarning(string text, int ms)
        {
            _warningText = text;
            _warningUntil = DateTime.Now.AddMilliseconds(ms);
        }
        private void DrawWarning()
        {
            if (_warningText == null || DateTime.Now > _warningUntil) return;
            new ContainerElement(new PointF(20, 20), new SizeF(360, 50), Color.FromArgb(200, 20, 20, 20)).Draw();
            new TextElement(_warningText, new PointF(30, 30), 0.28f, Color.FromArgb(255, 255, 120, 120), Font.ChaletLondon).Draw();
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
            if (_activeJob != ActiveJob.None) return;

            if (veh.IsEngineRunning)
            {
                float speedKmh = veh.Speed * 3.6f;
                float modifier = speedKmh < 5f ? 1.2f : (speedKmh > 100f ? 0.8f : 1.0f);
                if (veh.ClassType == VehicleClass.Sports || veh.ClassType == VehicleClass.Super) modifier *= 1.2f;
                if (veh.EngineHealth < 700f) modifier *= 1.3f;
                modifier = Math.Min(modifier, 1.8f) * _fuelConsumeRate;

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
                    ShowWarning("Out of fuel! Call a delivery from the F7 settings menu.", 4000);
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
        // DYNAMIC PRICE
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
            DrawWarning();

            if (_settingsMenu.Visible || _activeJob != ActiveJob.None) { _serviceMenu.Visible = false; return; }
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) { _serviceMenu.Visible = false; _nearStation = null; return; }
            Vehicle veh = playerPed.CurrentVehicle;
            if (veh == null || veh.Driver != playerPed || veh.Speed > 1.0f) { _serviceMenu.Visible = false; _nearStation = null; return; }

            var (station, type, pos) = FindNearbyMachine(veh.Position, 6f);
            _nearStation = station;
            _nearFuelType = type;
            if (station == null) { _serviceMenu.Visible = false; return; }

            if (station.OutOfStock)
            {
                ShowWarning($"{station.Name} is out of fuel right now - try another station.", 3000);
                _serviceMenu.Visible = false;
                return;
            }

            string required = GetRequiredFuelType(veh);
            if (type != required)
            {
                string audience = type == "Electric" ? "Hybrids" : type == "Diesel" ? "Diesel vehicles" :
                                   type == "Premium" ? "Sports/Super cars" : "Regular vehicles";
                ShowWarning($"{type.ToUpper()} PUMP - this pump is for {audience} only.", 3000);
                _serviceMenu.Visible = false;
                return;
            }

            // keep the menu's live numbers up to date while it's open
            float space = Math.Max(0f, _maxFuel - _fuel);
            _fuelAmountItem.Items = IntSteps(5, Math.Max(5, (int)space), 5).ToList();
            // NOTE: NativeListItem<T>.Items's setter does not clamp the selected
            // index - reusing a stale index after the list shrinks throws
            // IndexOutOfRangeException the next time SelectedItem is read. Always
            // reset to a safe index right after reassigning.
            if (_fuelAmountItem.Items.Count > 0) _fuelAmountItem.SelectedIndex = 0;
            float price = GetFuelPrice(type, station);
            _fuelAmountItem.Description = $"{type} @ ${price:F2}/L - Total: ${_fuelAmountItem.SelectedItem * price:F2}";
            _engineOilItem.Description = $"${(ENGINE_OIL_MAX - _engineOil) * ENGINE_OIL_PRICE_PER_UNIT:F2}";
            _transOilItem.Description = $"${(TRANS_OIL_MAX - _transOil) * TRANS_OIL_PRICE_PER_UNIT:F2}";
            _repairItem.Description = $"${GetRepairCost(veh):F2}";
            _useJerryCanItem.Description = $"{_jerryCanFuel:F1}L available";
            _serviceMenu.Name = $"{station.Name} ({type})";

            if (!_serviceMenu.Visible) _serviceMenu.Visible = true;
        }

        private void ConfirmRefuel(float litres)
        {
            if (litres <= 0.5f) { Notification.Show("~y~Tank is already full."); return; }
            float cost = litres * GetFuelPrice(_nearFuelType, _nearStation);
            StartJob(ActiveJob.Refuel, cost, "Refueling", litres);
        }

        private void ConfirmEngineOil()
        {
            float cost = (ENGINE_OIL_MAX - _engineOil) * ENGINE_OIL_PRICE_PER_UNIT;
            if (cost <= 0.5f) { Notification.Show("~y~Engine oil is already full."); return; }
            StartJob(ActiveJob.ChangeEngineOil, cost, "Changing engine oil", 0);
        }

        private void ConfirmTransOil()
        {
            float cost = (TRANS_OIL_MAX - _transOil) * TRANS_OIL_PRICE_PER_UNIT;
            if (cost <= 0.5f) { Notification.Show("~y~Transmission oil is already full."); return; }
            StartJob(ActiveJob.ChangeTransOil, cost, "Changing transmission oil", 0);
        }

        private void ConfirmRepair()
        {
            Vehicle veh = Game.Player.Character.CurrentVehicle;
            float cost = GetRepairCost(veh);
            if (cost <= 0.5f) { Notification.Show("~y~Your vehicle is in great condition."); return; }
            StartJob(ActiveJob.Repair, cost, "Repairing vehicle", 0);
        }

        private void UseJerryCan()
        {
            if (_jerryCanFuel <= 0.2f) { Notification.Show("~y~Jerry can is empty."); return; }
            float space = Math.Max(0f, _maxFuel - _fuel);
            float transfer = Math.Min(_jerryCanFuel, space);
            if (transfer <= 0.2f) { Notification.Show("~y~Tank is already full."); return; }
            _fuel += transfer;
            _jerryCanFuel -= transfer;
            Notification.Show($"~g~Poured {transfer:F1}L~w~ from your jerry can.");
            SaveData();
        }

        private void StartJob(ActiveJob job, float cost, string label, float litres)
        {
            if (_money < cost) { Notification.Show("~r~Your money is not enough."); return; }
            _money -= (int)Math.Round(cost);
            _activeJob = job;
            _jobTargetFuel = job == ActiveJob.Refuel ? Math.Min(_maxFuel, _fuel + litres) : 0f;
            _serviceMenu.Visible = false;
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
        // RANDOM STATION STOCKOUT
        // =================================================================
        private void UpdateStationStock()
        {
            if ((DateTime.Now - _lastStockCheck).TotalSeconds < 30) return;
            _lastStockCheck = DateTime.Now;

            foreach (var st in _stations)
            {
                if (st.OutOfStock && DateTime.Now >= st.OutOfStockUntil) st.OutOfStock = false;
                else if (!st.OutOfStock && Rand.Next(1000) < 3)
                {
                    st.OutOfStock = true;
                    st.OutOfStockUntil = DateTime.Now.AddMinutes(2 + Rand.Next(6));
                }
            }
        }

        // =================================================================
        // SIPHONING - progress bar, fills jerry can (triggered from Settings menu)
        // =================================================================
        private void TryStartSiphon()
        {
            Ped playerPed = Game.Player.Character;
            if (playerPed.IsInVehicle()) { Notification.Show("~r~Get out of your vehicle first."); return; }
            if (_jerryCanFuel >= JERRY_CAN_CAPACITY - 0.5f) { Notification.Show("~y~Jerry can is already full."); return; }

            Vehicle nearest = null;
            float bestDist = 4.0f;
            foreach (Vehicle v in World.GetNearbyVehicles(playerPed.Position, 4.0f))
            {
                if (v == null || !v.Exists() || v == playerPed.CurrentVehicle) continue;
                if (v.Driver != null && v.Driver.Exists() && !v.Driver.IsDead) continue;
                float d = v.Position.DistanceTo(playerPed.Position);
                if (d < bestDist) { bestDist = d; nearest = v; }
            }

            if (nearest == null) { Notification.Show("~r~No parked vehicle nearby."); return; }

            _theftTarget = nearest;
            _theftInProgress = true;
            _theftProgress = 0f;
            playerPed.Task.PlayAnimation("amb@prop_human_parking_meter@male@base", "base", 8.0f, -1, AnimationFlags.Loop);
            Notification.Show("~y~Siphoning fuel...~w~ hold still.");
        }

        private void UpdateSiphonProgress()
        {
            if (!_theftInProgress) return;
            Ped playerPed = Game.Player.Character;

            if (playerPed.IsInVehicle() || _theftTarget == null || !_theftTarget.Exists() ||
                _theftTarget.Position.DistanceTo(playerPed.Position) > 6f)
            {
                _theftInProgress = false;
                playerPed.Task.ClearAll();
                Notification.Show("~r~Siphoning interrupted.");
                return;
            }

            _theftProgress += Game.LastFrameTime / THEFT_DURATION;

            float barX = 20f, barY = 90f;
            new ContainerElement(new PointF(barX, barY), new SizeF(300, 30), Color.FromArgb(200, 20, 20, 20)).Draw();
            new ContainerElement(new PointF(barX + 5, barY + 20), new SizeF(290, 6), Color.FromArgb(150, 60, 60, 60)).Draw();
            new ContainerElement(new PointF(barX + 5, barY + 20), new SizeF(290 * Math.Min(1f, _theftProgress), 6), Color.Orange).Draw();
            new TextElement($"Siphoning fuel... {Math.Min(100, (int)(_theftProgress * 100))}%", new PointF(barX + 5, barY + 2), 0.24f, Color.White, Font.ChaletLondon).Draw();

            if (_theftProgress >= 1f)
            {
                _theftInProgress = false;
                playerPed.Task.ClearAll();

                float stolen = Math.Min(JERRY_CAN_CAPACITY - _jerryCanFuel, 5f + (float)Rand.NextDouble() * 10f);
                _jerryCanFuel += Math.Max(0f, stolen);
                Notification.Show($"~g~Siphoned {stolen:F1}L~w~ into your jerry can.");

                if (Rand.Next(100) < 25)
                {
                    Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, Game.Player, 1, false);
                    Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, Game.Player, false);
                    Notification.Show("~r~Someone saw you!");
                }
                SaveData();
            }
        }

        // =================================================================
        // FUEL DELIVERY BY PLANE
        // =================================================================
        private void TryCallDelivery()
        {
            if (_deliveryPhase != DeliveryPhase.None) { Notification.Show("~y~A delivery is already on its way."); return; }
            if (_money < DELIVERY_COST) { Notification.Show($"~r~Not enough money (${DELIVERY_COST})."); return; }

            Ped playerPed = Game.Player.Character;
            Vector3 playerPos = playerPed.Position;
            Vector3 spawnPos = playerPos + new Vector3(0, 0, 250f) + (playerPed.ForwardVector * 400f);

            _deliveryPlane = World.CreateVehicle(VehicleHash.Velum2, spawnPos, 0f);
            if (_deliveryPlane == null) { Notification.Show("~r~Delivery failed to launch."); return; }
            _deliveryPlane.IsPersistent = true;
            _deliveryPlane.Speed = 60f;

            _deliveryPilot = World.CreatePed(PedHash.Pilot01SMM, spawnPos);
            if (_deliveryPilot != null)
            {
                _deliveryPilot.IsPersistent = true;
                _deliveryPilot.SetIntoVehicle(_deliveryPlane, VehicleSeat.Driver);
                Function.Call(Hash.TASK_PLANE_CHASE, _deliveryPilot, playerPed.IsInVehicle() ? (Entity)playerPed.CurrentVehicle : playerPed, 0f);
            }

            _deliveryBlip = _deliveryPlane.AddBlip();
            _deliveryBlip.Sprite = BlipSprite.Plane;
            _deliveryBlip.Color = BlipColor.Yellow;
            _deliveryBlip.Name = "Fuel Delivery";
            _deliveryBlip.ShowRoute = true;

            _money -= DELIVERY_COST;
            _deliveryPhase = DeliveryPhase.Inbound;
            _deliveryFillProgress = 0f;
            Notification.Show($"~g~Fuel delivery dispatched~w~ (${DELIVERY_COST}). Watch the sky.");
        }

        private void UpdateDelivery()
        {
            if (_deliveryPhase == DeliveryPhase.None) return;
            Ped playerPed = Game.Player.Character;

            if (_deliveryPlane == null || !_deliveryPlane.Exists())
            {
                CleanupDelivery();
                return;
            }

            float dist = _deliveryPlane.Position.DistanceTo(playerPed.Position);

            switch (_deliveryPhase)
            {
                case DeliveryPhase.Inbound:
                    new TextElement($"Fuel delivery inbound: {dist:F0}m", new PointF(20, 60), 0.26f, Color.FromArgb(255, 255, 220, 120), Font.ChaletLondon).Draw();

                    if (dist < 40f)
                    {
                        bool boatOrAir = playerPed.IsInVehicle() &&
                            (playerPed.CurrentVehicle.ClassType == VehicleClass.Boats ||
                             playerPed.CurrentVehicle.ClassType == VehicleClass.Planes ||
                             playerPed.CurrentVehicle.ClassType == VehicleClass.Helicopters);

                        if (boatOrAir)
                        {
                            _deliveryPhase = DeliveryPhase.Hovering;
                            _deliveryFillProgress = 0f;
                        }
                        else
                        {
                            Vector3 dropPos = playerPed.Position + new Vector3(2f, 2f, 0f);
                            _deliveryCan = World.CreateProp(new Model("prop_jerrycan_01a"), dropPos, true, true);
                            _deliveryPhase = DeliveryPhase.WaitingPickup;
                            Notification.Show("~g~A jerry can was dropped nearby~w~ - go pick it up.");
                        }
                    }
                    break;

                case DeliveryPhase.Hovering:
                    new TextElement($"Fuel delivery hovering - filling... {(int)(_deliveryFillProgress * 100)}%", new PointF(20, 60), 0.26f, Color.FromArgb(255, 180, 255, 180), Font.ChaletLondon).Draw();

                    _deliveryFillProgress += Game.LastFrameTime / 8f;
                    _fuel = Math.Min(_maxFuel, _maxFuel * Math.Min(1f, _deliveryFillProgress));

                    if (_deliveryFillProgress >= 1f)
                    {
                        Notification.Show("~g~Tank filled by air delivery!");
                        SaveData();
                        _deliveryPhase = DeliveryPhase.Leaving;
                        _deliveryLeaveAt = DateTime.Now.AddSeconds(4);
                    }
                    break;

                case DeliveryPhase.WaitingPickup:
                    if (_deliveryCan == null || !_deliveryCan.Exists())
                    {
                        _deliveryPhase = DeliveryPhase.Leaving;
                        _deliveryLeaveAt = DateTime.Now;
                        break;
                    }
                    float canDist = _deliveryCan.Position.DistanceTo(playerPed.Position);
                    if (canDist < 15f)
                        new TextElement("Walk to the jerry can to collect it", new PointF(20, 60), 0.26f, Color.FromArgb(255, 220, 220, 255), Font.ChaletLondon).Draw();

                    if (canDist < 1.5f)
                    {
                        float added = Math.Min(JERRY_CAN_CAPACITY - _jerryCanFuel, 20f);
                        _jerryCanFuel += Math.Max(0f, added);
                        Notification.Show($"~g~Picked up the jerry can~w~ (+{added:F0}L).");
                        _deliveryCan.Delete();
                        _deliveryCan = null;
                        _deliveryPhase = DeliveryPhase.Leaving;
                        _deliveryLeaveAt = DateTime.Now;
                        SaveData();
                    }
                    break;

                case DeliveryPhase.Leaving:
                    if (DateTime.Now >= _deliveryLeaveAt) CleanupDelivery();
                    break;
            }
        }

        private void CleanupDelivery()
        {
            if (_deliveryBlip != null && _deliveryBlip.Exists()) _deliveryBlip.Delete();
            if (_deliveryCan != null && _deliveryCan.Exists()) _deliveryCan.Delete();
            if (_deliveryPilot != null && _deliveryPilot.Exists()) _deliveryPilot.Delete();
            if (_deliveryPlane != null && _deliveryPlane.Exists()) _deliveryPlane.Delete();
            _deliveryBlip = null; _deliveryCan = null; _deliveryPilot = null; _deliveryPlane = null;
            _deliveryPhase = DeliveryPhase.None;
        }

        // =================================================================
        // HUD - animated flat fuel tank + speed track, fully custom position
        // =================================================================
        private void UpdateHud()
        {
            float x = _hudOffsetX;
            float barTop = Screen.Height + _hudOffsetY; // _hudOffsetY is negative
            float tankWidth = 200f * _hudScale, tankHeight = 46f * _hudScale;

            float fuelPct = _maxFuel > 0 ? _fuel / _maxFuel : 0f;
            _displayedFuelPct += (fuelPct - _displayedFuelPct) * Math.Min(1f, Game.LastFrameTime * 2.5f);

            // outer tank shell
            new ContainerElement(new PointF(x - 8, barTop - 26), new SizeF(tankWidth + 16, tankHeight + 40), Color.FromArgb(150, 0, 0, 0)).Draw();
            new TextElement("FUEL", new PointF(x, barTop - 22), 0.24f, Color.White, Font.ChaletLondon).Draw();
            new ContainerElement(new PointF(x, barTop), new SizeF(tankWidth, tankHeight), Color.FromArgb(255, 25, 25, 25)).Draw();

            Color fuelColor = _displayedFuelPct < 0.15f ? Color.Red : (_displayedFuelPct < 0.3f ? Color.Orange : Color.LimeGreen);
            new ContainerElement(new PointF(x + 4, barTop + 4), new SizeF((tankWidth - 8) * _displayedFuelPct, tankHeight - 8), fuelColor).Draw();
            new TextElement($"{fuelPct * 100:F0}%", new PointF(x + tankWidth / 2 - 15, barTop + tankHeight / 2 - 10), 0.3f, Color.White, Font.ChaletLondon).Draw();

            float oilPct = _engineOil / ENGINE_OIL_MAX;
            Color oilColor = oilPct < 0.3f ? Color.Red : Color.LimeGreen;
            new TextElement("OIL", new PointF(x, barTop + tankHeight + 6), 0.2f, Color.White, Font.ChaletLondon).Draw();
            new ContainerElement(new PointF(x, barTop + tankHeight + 22), new SizeF(tankWidth, 8), Color.FromArgb(140, 60, 60, 60)).Draw();
            new ContainerElement(new PointF(x, barTop + tankHeight + 22), new SizeF(tankWidth * oilPct, 8), oilColor).Draw();

            if (_jerryCanFuel > 0.1f)
                new TextElement($"Jerry can: {_jerryCanFuel:F0}/{JERRY_CAN_CAPACITY:F0}L", new PointF(x, barTop + tankHeight + 36), 0.2f, Color.LightBlue, Font.ChaletLondon).Draw();

            // speed track beside the tank
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) return;
            Vehicle veh = playerPed.CurrentVehicle;
            if (veh == null || !veh.Exists()) return;

            float trackX = x + tankWidth + 30f;
            float trackTop = barTop - 10f, trackHeight = tankHeight + 30f;
            new ContainerElement(new PointF(trackX, trackTop), new SizeF(2, trackHeight), Color.FromArgb(160, 220, 220, 220)).Draw();

            float maxSpeed = Function.Call<float>(Hash.GET_VEHICLE_MODEL_ESTIMATED_MAX_SPEED, veh.Model.Hash);
            float speedPct = Math.Min(1f, Math.Max(0f, veh.Speed / Math.Max(1f, maxSpeed)));
            float markerY = trackTop + trackHeight - (speedPct * trackHeight);
            Color markerColor = speedPct < 0.7f ? Color.LimeGreen : (speedPct < 0.9f ? Color.Orange : Color.Red);
            new ContainerElement(new PointF(trackX - 6, markerY - 2), new SizeF(14, 4), markerColor).Draw();

            int kmh = (int)(veh.Speed * 3.6f);
            new TextElement($"{kmh}", new PointF(trackX - 8, trackTop - 22), 0.28f, Color.White, Font.ChaletLondon).Draw();
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
        // PERSISTENCE
        // =================================================================
        private void LoadSaveData()
        {
            try
            {
                if (!File.Exists(SavePath)) return;
                foreach (var line in File.ReadAllLines(SavePath))
                {
                    var parts = line.Split('|');
                    if (parts.Length == 5 &&
                        float.TryParse(parts[1], out float f) &&
                        float.TryParse(parts[2], out float eo) &&
                        float.TryParse(parts[3], out float to) &&
                        float.TryParse(parts[4], out float jc))
                    {
                        _savedByPlate[parts[0]] = new[] { f, eo, to };
                        _jerryCanFuel = jc; // global, not per-vehicle
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

                var lines = _savedByPlate.Select(kv => $"{kv.Key}|{kv.Value[0]:F1}|{kv.Value[1]:F1}|{kv.Value[2]:F1}|{_jerryCanFuel:F1}");
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
                _fuel = _maxFuel * 0.5f;
                _engineOil = ENGINE_OIL_MAX;
                _transOil = TRANS_OIL_MAX;
            }

            _displayedFuelPct = _maxFuel > 0 ? _fuel / _maxFuel : 1f;
            _radioOn = true;
        }

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
                    _hudOffsetX = float.Parse(lines[0]);
                    _hudOffsetY = float.Parse(lines[1]);
                    _hudScale = float.Parse(lines[2]);
                }
            }
            catch (Exception ex) { Log("HUD config load error: " + ex.Message); }
        }

        private void SaveHudConfig()
        {
            try
            {
                var lines = new[] { _hudOffsetX.ToString("F0"), _hudOffsetY.ToString("F0"), _hudScale.ToString("F1") };
                File.WriteAllLines(HudConfigPath, lines);
            }
            catch (Exception ex) { Log("HUD config save error: " + ex.Message); }
        }

        // =================================================================
        // KEY HANDLING - just F7 (settings) and X (radio); menus themselves
        // are driven entirely by arrow keys + Enter via LemonUI.
        // =================================================================
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.KeyCode == Keys.F7)
                {
                    bool opening = !_settingsMenu.Visible;
                    _settingsMenu.Visible = opening;
                    if (!opening) { SaveConfig(); SaveHudConfig(); }
                    return;
                }

                if (_settingsMenu.Visible || _serviceMenu.Visible) return; // let LemonUI handle nav keys

                if (e.KeyCode == Keys.X) ToggleRadio();
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
