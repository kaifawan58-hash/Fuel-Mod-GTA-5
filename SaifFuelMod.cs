// SaifFuelMod.cs
// C# / ScriptHookVDotNet3 port of the original Lua fuel mod, rebuilt with
// LemonUI for native-look menus (colored header, highlighted selection,
// "X / Y" counter - navigated with arrow keys + Enter/Backspace, same as
// GTA's own interaction menu).
//
// Systems:
//  - Approach the correct pump -> a LemonUI menu opens on its own: Fuel,
//    Repair Vehicle, Buy Now Use Later.
//  - F7 opens Settings: consumption/refuel rates, prices, HUD position/scale.
//  - Fuel delivery: call it with H, a helicopter flies in, hovers over you
//    with a hose visual and fills your tank with a progress bar.
//  - Fuel quality varies per station (shown by pump blip color) and affects
//    vehicle top speed after refueling there.
//  - Real per-machine station coordinates, dynamic hour/weekend/weather/
//    region pricing, random pump stockouts, per-vehicle fuel that persists
//    automatically, ambient refuelling traffic, aircraft missile-threat radar.

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
        private class Machine
        {
            public Vector3 Position;
            // 0.80-1.20 - drives the vehicle top-speed modifier after
            // refueling from THIS specific pump (see ApplyFuelQualityToVehicle).
            // Every machine at a station rolls its own quality, so even
            // pumps standing right next to each other at the same station
            // can differ - matches how a real station can have one pump
            // running cleaner fuel than its neighbor.
            public float Quality = 1f;
        }

        private class Station
        {
            public string Name;
            public Dictionary<string, Machine> Machines = new Dictionary<string, Machine>();
            public bool IsMountain;
            public float RegionPriceMult = 1f;
            public bool OutOfStock;
            public DateTime OutOfStockUntil = DateTime.MinValue;
            // Average of all machines - only used for the map blip color,
            // giving a rough "overall" read of this station before arriving.
            public float FuelQuality => Machines.Count > 0 ? Machines.Values.Average(m => m.Quality) : 1f;
        }

        private readonly List<Station> _stations = new List<Station>();

        private void BuildStations()
        {
            void Add(string name, params (string type, float x, float y, float z)[] machines)
            {
                var s = new Station { Name = name };
                foreach (var m in machines)
                {
                    s.Machines[m.type] = new Machine
                    {
                        Position = new Vector3(m.x, m.y, m.z),
                        Quality = 0.80f + (float)Rand.NextDouble() * 0.40f
                    };
                }

                float avgZ = 0f;
                foreach (var mv in s.Machines.Values) avgZ += mv.Position.Z;
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
        private float _refuelSpeed = 4.0f;
        private float _priceRegular = 1.50f;
        private float _priceDiesel = 1.42f;
        private float _pricePremium = 1.65f;
        private float _priceElectric = 0.45f;
        private const string DataPath = "scripts\\SaifFuelMod_data.txt";

        // HUD placement/scale - fully custom position, no preset corners
        private float _hudOffsetX = 1220f;
        private float _hudOffsetY = -50f; // negative = measured up from bottom of screen
        private float _hudScale = 0.6f;
        private float _displayedFuelPct = 1f; // lerped toward the real value for a live-draining look
        private float _displayedUsagePct = 0f; // lerped current fuel-burn rate, for the usage line
        private float _displayedThrustPct = 0f; // lerped jet thrust readout

        // =================================================================
        // VEHICLE STATE
        // =================================================================
        private float _fuel = 30f;
        private float _maxFuel = 60f;
        private readonly Dictionary<string, float> _savedByPlate = new Dictionary<string, float>();
        private int _lastVehicleHandle = -1;
        private bool _radioOn = true;
        private float _currentFuelQuality = 1f; // last station's quality, drives top-speed modifier
        private float _prepaidLitres = 0f; // "Buy Now, Use Later" banked credit

        private int _money = 5000;

        private static readonly Random Rand = new Random();

        private DateTime _slowConsumptionUntil = DateTime.MinValue;
        private DateTime _engineStartTime = DateTime.MinValue;
        private bool _wasEngineRunning = false;
        private bool _stationsSpawned = false;
        private DateTime _lastStockCheck = DateTime.MinValue;

        // Pump proximity
        private Station _nearStation = null;
        private string _nearFuelType = null;
        private float _nearMachineQuality = 1f;

        private enum ActiveJob { None, Refuel, Repair }
        private ActiveJob _activeJob = ActiveJob.None;
        private float _jobTargetFuel = 0f;
        private float _jobStartValue = 0f;

        // Fuel delivery by helicopter
        private enum DeliveryPhase { None, Inbound, Descending, Hovering, Leaving }
        private DeliveryPhase _deliveryPhase = DeliveryPhase.None;
        private Vehicle _deliveryPlane = null;
        private Ped _deliveryPilot = null;
        private Blip _deliveryBlip = null;
        private float _deliveryFillProgress = 0f;
        private int _deliveryCost = 120;

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
        private int _fuelAmountItemCapLitres = -1;
        private NativeItem _customAmountItem;
        private NativeItem _repairItem;
        private NativeItem _buyNowUseLaterItem;

        private NativeMenu _settingsMenu;
        private NativeListItem<float> _fuelRateItem;
        private NativeListItem<float> _refuelSpeedItem;
        private NativeListItem<float> _priceRegularItem;
        private NativeListItem<float> _priceDieselItem;
        private NativeListItem<float> _pricePremiumItem;
        private NativeListItem<int> _deliveryCostItem;
        private NativeListItem<int> _hudOffsetXItem;
        private NativeListItem<int> _hudOffsetYItem;
        private NativeListItem<float> _hudScaleItem;
        private NativeItem _hudSetExactItem;
        private NativeItem _hudMoveLiveItem;

        public FuelMod()
        {
            try
            {
                BuildStations();
                LoadAllData();
                BuildMenus();
            }
            catch (Exception ex)
            {
                // If ANY of the above throws (e.g. a leftover/corrupt save
                // file from an older version of the mod), the whole script
                // previously died silently with nothing visible in-game -
                // that's the classic "mod doesn't work at all" symptom.
                // Now it's logged and Tick/KeyDown still get hooked below.
                Log("FuelMod() init error: " + ex.Message + "\r\n" + ex.StackTrace);
            }

            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += (s, e) => { SaveAllData(); CleanupDelivery(); ClearThreatBlips(); ClearPlayerMissiles(); ClearEnemyMissiles(); ClearFlares(); };
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
            _customAmountItem = new NativeItem("Custom Amount...", "Type an exact litre amount to fill");
            _repairItem = new NativeItem("Repair Vehicle");
            _buyNowUseLaterItem = new NativeItem("Buy Now, Use Later");

            _serviceMenu.Add(_fuelAmountItem);
            _serviceMenu.Add(_customAmountItem);
            _serviceMenu.Add(_repairItem);
            _serviceMenu.Add(_buyNowUseLaterItem);
            _serviceMenu.ItemActivated += ServiceMenu_ItemActivated;
            _pool.Add(_serviceMenu);

            // ---- Settings menu (F7) ----
            _settingsMenu = new NativeMenu("Saif Fuel Mod", "Settings", "");

            _fuelRateItem = new NativeListItem<float>("Fuel Consume Rate", FloatSteps(0.2f, 3.0f, 0.1f));
            SelectClosest(_fuelRateItem, _fuelConsumeRate);
            _fuelRateItem.ItemChanged += (s, e) => _fuelConsumeRate = _fuelRateItem.SelectedItem;

            _refuelSpeedItem = new NativeListItem<float>("Refuel Speed (L/s)", FloatSteps(1.0f, 20.0f, 0.5f));
            SelectClosest(_refuelSpeedItem, _refuelSpeed);
            _refuelSpeedItem.ItemChanged += (s, e) => _refuelSpeed = _refuelSpeedItem.SelectedItem;

            _priceRegularItem = new NativeListItem<float>("Regular Price", FloatSteps(0.5f, 15f, 0.1f));
            SelectClosest(_priceRegularItem, _priceRegular);
            _priceRegularItem.ItemChanged += (s, e) => _priceRegular = _priceRegularItem.SelectedItem;

            _priceDieselItem = new NativeListItem<float>("Diesel Price", FloatSteps(0.5f, 15f, 0.1f));
            SelectClosest(_priceDieselItem, _priceDiesel);
            _priceDieselItem.ItemChanged += (s, e) => _priceDiesel = _priceDieselItem.SelectedItem;

            _pricePremiumItem = new NativeListItem<float>("Premium Price", FloatSteps(0.5f, 15f, 0.1f));
            SelectClosest(_pricePremiumItem, _pricePremium);
            _pricePremiumItem.ItemChanged += (s, e) => _pricePremium = _pricePremiumItem.SelectedItem;

            _deliveryCostItem = new NativeListItem<int>("Air Delivery Price", IntSteps(20, 500, 10));
            SelectClosest(_deliveryCostItem, _deliveryCost);
            _deliveryCostItem.ItemChanged += (s, e) => _deliveryCost = _deliveryCostItem.SelectedItem;

            _hudOffsetXItem = new NativeListItem<int>("HUD X Position", IntSteps(0, 1800, 10));
            SelectClosest(_hudOffsetXItem, (int)_hudOffsetX);
            _hudOffsetXItem.ItemChanged += (s, e) => _hudOffsetX = _hudOffsetXItem.SelectedItem;

            _hudOffsetYItem = new NativeListItem<int>("HUD Y Position (from bottom)", IntSteps(-800, -50, 10));
            SelectClosest(_hudOffsetYItem, (int)_hudOffsetY);
            _hudOffsetYItem.ItemChanged += (s, e) => _hudOffsetY = _hudOffsetYItem.SelectedItem;

            _hudScaleItem = new NativeListItem<float>("HUD Size", FloatSteps(0.6f, 1.6f, 0.1f));
            SelectClosest(_hudScaleItem, _hudScale);
            _hudScaleItem.ItemChanged += (s, e) => _hudScale = _hudScaleItem.SelectedItem;

            _hudSetExactItem = new NativeItem("Set HUD Position/Size...", "Type exact X,Y,Scale (e.g. 285,-260,1.0)");
            _hudMoveLiveItem = new NativeItem("Move HUD (Live)...", "Arrow keys to move, +/- to resize, Enter to confirm");

            _settingsMenu.Add(_fuelRateItem);
            _settingsMenu.Add(_refuelSpeedItem);
            _settingsMenu.Add(_priceRegularItem);
            _settingsMenu.Add(_priceDieselItem);
            _settingsMenu.Add(_pricePremiumItem);
            _settingsMenu.Add(_deliveryCostItem);
            _settingsMenu.Add(_hudOffsetXItem);
            _settingsMenu.Add(_hudOffsetYItem);
            _settingsMenu.Add(_hudScaleItem);
            _settingsMenu.Add(_hudSetExactItem);
            _settingsMenu.Add(_hudMoveLiveItem);
            _settingsMenu.ItemActivated += SettingsMenu_ItemActivated;
            _pool.Add(_settingsMenu);
        }

        private void ServiceMenu_ItemActivated(object sender, ItemActivatedArgs e)
        {
            if (e.Item == _fuelAmountItem) ConfirmRefuel(_fuelAmountItem.SelectedItem);
            else if (e.Item == _customAmountItem) ConfirmCustomRefuel();
            else if (e.Item == _repairItem) ConfirmRepair();
            else if (e.Item == _buyNowUseLaterItem) BuyNowUseLater();
        }

        private void SettingsMenu_ItemActivated(object sender, ItemActivatedArgs e)
        {
            if (e.Item == _hudSetExactItem) SetHudExact();
            else if (e.Item == _hudMoveLiveItem) StartHudLiveMove();
        }

        // =================================================================
        // LIVE HUD POSITIONING - trainer/menu-style: closes all menus,
        // arrow keys nudge X/Y, +/- (or PageUp/PageDown) resize, Enter saves
        // and exits, Backspace/Escape cancels back to the pre-move position.
        // =================================================================
        private bool _hudMoveMode = false;
        private float _hudMoveRevertX, _hudMoveRevertY, _hudMoveRevertScale;

        private void StartHudLiveMove()
        {
            _hudMoveRevertX = _hudOffsetX;
            _hudMoveRevertY = _hudOffsetY;
            _hudMoveRevertScale = _hudScale;
            _hudMoveMode = true;
            _settingsMenu.Visible = false;
            ShowToast("~g~Move HUD:~w~ Arrows = move, +/- = resize, Enter = save, Backspace = cancel");
        }

        private void HandleHudLiveMoveKey(Keys key)
        {
            const float step = 12f;
            switch (key)
            {
                case Keys.Left: _hudOffsetX = Math.Max(0f, _hudOffsetX - step); break;
                case Keys.Right: _hudOffsetX = Math.Min(1800f, _hudOffsetX + step); break;
                case Keys.Up: _hudOffsetY = Math.Max(-800f, _hudOffsetY - step); break;
                case Keys.Down: _hudOffsetY = Math.Min(-50f, _hudOffsetY + step); break;
                case Keys.Oemplus:
                case Keys.Add:
                    _hudScale = Math.Min(2.5f, _hudScale + 0.05f); break;
                case Keys.OemMinus:
                case Keys.Subtract:
                    _hudScale = Math.Max(0.4f, _hudScale - 0.05f); break;
                case Keys.Enter:
                    _hudMoveMode = false;
                    SelectClosest(_hudOffsetXItem, (int)_hudOffsetX);
                    SelectClosest(_hudOffsetYItem, (int)_hudOffsetY);
                    SelectClosest(_hudScaleItem, _hudScale);
                    ShowToast($"~g~HUD position saved~w~ ({(int)_hudOffsetX},{(int)_hudOffsetY} @ {_hudScale:F1}x)");
                    SaveAllData();
                    break;
                case Keys.Back:
                case Keys.Escape:
                    _hudOffsetX = _hudMoveRevertX;
                    _hudOffsetY = _hudMoveRevertY;
                    _hudScale = _hudMoveRevertScale;
                    _hudMoveMode = false;
                    ShowToast("~y~HUD move cancelled.");
                    break;
            }
        }

        // Lets the player type exact "X,Y,Scale" instead of scrolling the
        // list items - covers "set wherever I want" + exact fuel bar size.
        private void SetHudExact()
        {
            string input = Game.GetUserInput(WindowTitle.EnterMessage60, $"{(int)_hudOffsetX},{(int)_hudOffsetY},{_hudScale:F1}", 20);
            if (string.IsNullOrWhiteSpace(input)) return;

            var parts = input.Split(',');
            if (parts.Length != 3 ||
                !float.TryParse(parts[0], out float x) ||
                !float.TryParse(parts[1], out float y) ||
                !float.TryParse(parts[2], out float scale))
            {
                ShowToast("~r~Format must be X,Y,Scale (e.g. 285,-260,1.0)");
                return;
            }

            _hudOffsetX = Math.Max(0f, Math.Min(1800f, x));
            _hudOffsetY = Math.Max(-800f, Math.Min(-50f, y));
            _hudScale = Math.Max(0.4f, Math.Min(2.5f, scale));

            SelectClosest(_hudOffsetXItem, (int)_hudOffsetX);
            SelectClosest(_hudOffsetYItem, (int)_hudOffsetY);
            SelectClosest(_hudScaleItem, _hudScale);

            ShowToast($"~g~HUD set~w~ to {(int)_hudOffsetX},{(int)_hudOffsetY} @ {_hudScale:F1}x");
            SaveAllData();
        }

        // =================================================================
        // TICK
        // =================================================================
        private void OnTick(object sender, EventArgs e)
        {
            if (_pool == null) return; // BuildMenus() failed in the constructor - see the log file for why
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
                UpdateFuelDrain();
                UpdatePrepaidAutoFill();
                AutoRememberFuel();
                UpdateHud();
                UpdateStationStock();
                UpdatePumpProximity();
                UpdateActiveJob();
                UpdateDelivery();
                UpdateAmbientVehicles();
                UpdateAircraftThreatRadar();
                DrawToasts();
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

            string plate = Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, veh)?.Trim();
            if (string.IsNullOrEmpty(plate)) return;
            _savedByPlate[plate] = _fuel;
        }

        // Every pump gets the SAME blip name + sprite ("Saif Fuel Station").
        // GTA's own pause-map blip list groups blips that share a name+sprite
        // into one collapsible row with an arrow you cycle through - this is
        // what actually links all the pumps under one shared icon on the
        // right-side list, no custom clustering code needed. Blip COLOR is
        // driven by that station's fuel quality so you can tell good/bad
        // pumps apart on the map before you even arrive.
        private const string SHARED_BLIP_NAME = "Saif Fuel Station";
        private void CreateStationBlips(Station st)
        {
            Vector3 anchor = st.Machines.Values.First().Position;
            Blip b = World.CreateBlip(anchor);
            b.Sprite = BlipSprite.JerryCan;
            b.Color = st.FuelQuality >= 1.05f ? BlipColor.Green
                    : st.FuelQuality <= 0.95f ? BlipColor.Red
                    : BlipColor.Yellow;
            b.Name = SHARED_BLIP_NAME;
            b.IsShortRange = true;
            Function.Call(Hash.SET_BLIP_CATEGORY, b, 7);
        }

        // =================================================================
        // TOP-LEFT WARNING TEXT (replaces center-screen subtitles)
        // =================================================================
        // =================================================================
        // TOAST NOTIFICATIONS - replaces native GTA.UI.Notification.Show
        // everywhere in the mod. Styled to match LemonUI's own menu skin
        // (dark panel, colored left accent, white body text) so every
        // message - progress, warnings, purchases, everything - looks like
        // part of the same UI instead of the yellow native notification box.
        // =================================================================
        private class Toast { public string Text; public DateTime ExpiresAt; }
        private readonly List<Toast> _toasts = new List<Toast>();
        private const int TOAST_DURATION_MS = 3500;
        private const int MAX_TOASTS = 4;

        private void ShowToast(string text)
        {
            // strip GTA's ~r~/~g~/~w~ color tags - we render color via the
            // panel accent instead, so raw tags would just show as text.
            string clean = System.Text.RegularExpressions.Regex.Replace(text, "~.~", "");
            _toasts.Add(new Toast { Text = clean, ExpiresAt = DateTime.Now.AddMilliseconds(TOAST_DURATION_MS) });
            if (_toasts.Count > MAX_TOASTS) _toasts.RemoveAt(0);
        }

        private void DrawToasts()
        {
            _toasts.RemoveAll(t => DateTime.Now > t.ExpiresAt);
            if (_toasts.Count == 0) return;

            float w = 340f, h = 44f, gap = 6f;
            float x = Screen.Width - w - 20f;
            float y = 20f;

            foreach (var t in _toasts)
            {
                new ContainerElement(new PointF(x, y), new SizeF(w, h), Color.FromArgb(215, 25, 25, 25)).Draw();
                new ContainerElement(new PointF(x, y), new SizeF(4, h), Color.FromArgb(255, 90, 200, 120)).Draw(); // accent bar
                new TextElement(t.Text, new PointF(x + 14, y + 12), 0.28f, Color.White, Font.ChaletLondon).Draw();
                y += h + gap;
            }
        }

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
        // FUEL DRAIN
        // =================================================================
        private float _currentUsageLps = 0f; // litres/sec right now, drives the HUD usage line

        private void UpdateFuelDrain()
        {
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) { _currentUsageLps = 0f; return; }
            Vehicle veh = playerPed.CurrentVehicle;
            if (veh == null || veh.Driver != playerPed) { _currentUsageLps = 0f; return; }

            _maxFuel = GetTankSize(veh);
            if (_activeJob != ActiveJob.None) { _currentUsageLps = 0f; return; }

            if (veh.IsEngineRunning)
            {
                if (!_wasEngineRunning) _engineStartTime = DateTime.Now;
                _wasEngineRunning = true;

                float rpm = veh.CurrentRPM; // 0-1
                float throttle = Game.GetControlValueNormalized(GTA.Control.VehicleAccelerate);
                float brake = Game.GetControlValueNormalized(GTA.Control.VehicleBrake);
                float loadFactor = Math.Max(0.15f, Math.Min(1f, rpm * 0.5f + throttle * 0.5f));

                // Coasting/braking: foot off the gas (and/or braking) means
                // the engine isn't doing work even if RPM is still showing
                // from momentum - burn drops toward idle instead of tracking
                // leftover RPM, matching real fuel-cut-on-overrun behavior.
                bool coasting = throttle < 0.05f;
                if (coasting) loadFactor = brake > 0.1f ? 0.10f : Math.Min(loadFactor, 0.25f);

                float speedKmh = veh.Speed * 3.6f;
                float speedNorm = Math.Min(1f, speedKmh / 180f);

                // Slope + off-road now multiply the LOAD component, not raw
                // speed - matches real life: climbing a mountain SLOWLY
                // under heavy throttle still burns a lot because the engine
                // is working hard against gravity/terrain, regardless of
                // how fast you're actually moving. A flat, light-throttle
                // cruise barely uses anything even at speed.
                float pitchDeg = Function.Call<float>(Hash.GET_ENTITY_PITCH, veh);
                float slopePct = Math.Max(0f, Math.Min(1f, pitchDeg / 20f)); // 0 flat -> 1.0 at ~20deg+ climb
                float slopeMult = 1f + slopePct * 1.1f; // climbing hard can more than double the load burn

                bool onRoad = Function.Call<bool>(Hash.IS_POINT_ON_ROAD, veh.Position.X, veh.Position.Y, veh.Position.Z, veh);
                float terrainMult = onRoad ? 1f : 1.4f;

                // Heavier vehicle classes genuinely burn more per unit of
                // load in real life - a loaded truck/SUV works its engine
                // harder than a hatchback at the same throttle position.
                float weightMult = 1f;
                switch (veh.ClassType)
                {
                    case VehicleClass.Industrial:
                    case VehicleClass.Commercial:
                    case VehicleClass.Utility: weightMult = 1.35f; break;
                    case VehicleClass.SUVs:
                    case VehicleClass.Vans: weightMult = 1.15f; break;
                    case VehicleClass.Motorcycles: weightMult = 0.7f; break;
                }

                const float idleBase = 0.08f; // small constant burn just from the engine running, even parked
                float loadBurn = loadFactor * terrainMult * slopeMult * weightMult; // primary driver - how hard the engine works
                float aeroBurn = speedNorm * speedNorm * 0.35f; // secondary - only matters at real speed (drag), negligible slow

                float modifier = idleBase + loadBurn + aeroBurn;

                if (veh.ClassType == VehicleClass.Sports || veh.ClassType == VehicleClass.Super) modifier *= 1.1f;
                if (veh.EngineHealth < 700f) modifier *= 1.2f;

                // Startup burst: a short rich-burn spike right after ignition
                // (~1.5s), then settles into the load-based curve above -
                // and every time the throttle is tapped after idling
                // (stop-start driving), loadFactor itself spikes naturally,
                // giving the same "burst then settle" feel without a
                // separate special case.
                double sinceStart = (DateTime.Now - _engineStartTime).TotalSeconds;
                if (sinceStart < 1.5) modifier *= (float)(2.0 - (sinceStart / 1.5) * 1.0);

                modifier = Math.Min(modifier, 2.6f) * _fuelConsumeRate;
                if (DateTime.Now < _slowConsumptionUntil) modifier *= 0.5f;

                float litresPerSecond = (6.5f / 100f) * modifier;
                _currentUsageLps = litresPerSecond;
                _fuel = Math.Max(0f, _fuel - litresPerSecond * Game.LastFrameTime);

                if (_fuel <= 0f)
                {
                    veh.IsEngineRunning = false;
                    ShowWarning("Out of fuel! Call a delivery from the F7 settings menu.", 4000);
                }
            }
            else
            {
                _wasEngineRunning = false;
                _currentUsageLps = 0f;
            }
        }

        // NOTE: there is no reliable public "set vehicle fuel level" native
        // exposed by this SHVDN version (GTA V doesn't have a real fuel
        // system by default - only third-party mods simulate one, same as
        // this one does). Removed the sync attempt since the hash isn't
        // available here; the mod's own _fuel value stays the single source
        // of truth and drives the custom HUD bar.

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

        // Fuel quality (0.85-1.15 from the station) becomes a top-speed
        // modifier on the vehicle you just filled - bad fuel measurably
        // slows the car down, good fuel gives a small boost, using GTA's own
        // native percentage-modifier system so it stacks safely with mods.
        private void ApplyFuelQualityToVehicle(float quality)
        {
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) return;
            Vehicle veh = playerPed.CurrentVehicle;
            if (veh == null || !veh.Exists()) return;

            _currentFuelQuality = quality;
            float percentModifier = quality - 1.0f; // e.g. 0.85 quality -> -15%, 1.15 -> +15%
            Function.Call(Hash.MODIFY_VEHICLE_TOP_SPEED, veh, percentModifier);
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

        private (Station, string, Vector3, float) FindNearbyMachine(Vector3 pos, float range)
        {
            foreach (var st in _stations)
                foreach (var kvp in st.Machines)
                    if (kvp.Value.Position.DistanceTo(pos) <= range)
                        return (st, kvp.Key, kvp.Value.Position, kvp.Value.Quality);
            return (null, null, Vector3.Zero, 1f);
        }

        private void UpdatePumpProximity()
        {
            DrawWarning();

            if (_settingsMenu.Visible || _activeJob != ActiveJob.None) { _serviceMenu.Visible = false; return; }
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) { _serviceMenu.Visible = false; _nearStation = null; return; }
            Vehicle veh = playerPed.CurrentVehicle;
            if (veh == null || veh.Driver != playerPed || veh.Speed > 1.0f) { _serviceMenu.Visible = false; _nearStation = null; return; }

            var (station, type, pos, quality) = FindNearbyMachine(veh.Position, 6f);
            _nearStation = station;
            _nearFuelType = type;
            _nearMachineQuality = quality;
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

            // keep the menu's live numbers up to date while it's open.
            // BUG FIX: this used to reassign _fuelAmountItem.Items AND force
            // SelectedIndex back to 0 every single tick (~60x/sec), which wiped
            // out whatever amount the player had scrolled to before they could
            // ever press Enter - the pump always dispensed 5L no matter what
            // was shown. Now the list is only rebuilt when the max cap
            // actually changes, and the selected index is preserved (only
            // clamped if it's now out of range).
            int maxLitres = Math.Max(5, (int)Math.Max(0f, _maxFuel - _fuel));
            if (maxLitres != _fuelAmountItemCapLitres)
            {
                int prevSelected = _fuelAmountItem.Items.Count > 0 ? _fuelAmountItem.SelectedItem : 5;
                _fuelAmountItem.Items = IntSteps(5, maxLitres, 5).ToList();
                _fuelAmountItemCapLitres = maxLitres;
                int idx = _fuelAmountItem.Items.IndexOf(prevSelected);
                _fuelAmountItem.SelectedIndex = idx >= 0 ? idx : 0;
            }

            float price = GetFuelPrice(type, station);
            _fuelAmountItem.Description = $"{type} @ ${price:F2}/L - Total: ${_fuelAmountItem.SelectedItem * price:F2}";
            _customAmountItem.Description = "Type an exact litre amount to fill";
            _repairItem.Description = $"${GetRepairCost(veh):F2}";
            _buyNowUseLaterItem.Description = $"{_nearMachineQuality * 100f:F0}% quality - pay now, saved for later at this pump's price";
            _serviceMenu.Name = $"{station.Name} ({type})";

            if (!_serviceMenu.Visible) _serviceMenu.Visible = true;
        }

        private void ConfirmRefuel(float litres)
        {
            if (litres <= 0.5f) { ShowToast("~y~Tank is already full."); return; }
            float cost = litres * GetFuelPrice(_nearFuelType, _nearStation);
            StartJob(ActiveJob.Refuel, cost, "Refueling", litres);
            if (_nearStation != null) ApplyFuelQualityToVehicle(_nearMachineQuality);
        }

        private void ConfirmCustomRefuel()
        {
            float space = Math.Max(0f, _maxFuel - _fuel);
            if (space <= 0.5f) { ShowToast("~y~Tank is already full."); return; }

            string input = Game.GetUserInput(WindowTitle.EnterMessage60, $"{(int)space}", 5);
            if (string.IsNullOrWhiteSpace(input)) return;
            if (!float.TryParse(input, out float litres) || litres <= 0f)
            {
                ShowToast("~r~Enter a valid number of litres.");
                return;
            }

            litres = Math.Min(litres, space);
            ConfirmRefuel(litres);
        }

        private void ConfirmRepair()
        {
            Vehicle veh = Game.Player.Character.CurrentVehicle;
            float cost = GetRepairCost(veh);
            if (cost <= 0.5f) { ShowToast("~y~Your vehicle is in great condition."); return; }
            StartJob(ActiveJob.Repair, cost, "Repairing vehicle", 0);
        }

        // "Buy Now, Use Later" - pay for litres at today's pump price and
        // bank them as prepaid credit (no jerry can needed). Whenever your
        // tank later runs low, the banked litres auto-drip in until they run
        // out or the tank is full - see UpdatePrepaidAutoFill().
        private void BuyNowUseLater()
        {
            string input = Game.GetUserInput(WindowTitle.EnterMessage60, "20", 5);
            if (string.IsNullOrWhiteSpace(input)) return;
            if (!float.TryParse(input, out float litres) || litres <= 0f)
            {
                ShowToast("~r~Enter a valid number of litres.");
                return;
            }

            float cost = litres * GetFuelPrice(_nearFuelType, _nearStation);
            if (_money < cost) { ShowToast("~r~Your money is not enough."); return; }

            _money -= (int)Math.Round(cost);
            _prepaidLitres += litres;
            ShowToast($"~g~Bought {litres:F1}L~w~ for later (${cost:F2}) - auto-fills your tank when it runs low.");
            SaveAllData();
        }

        // Auto-drip banked litres into the tank once it's running low - this
        // is what makes prepaid fuel actually "use later" without needing to
        // visit a pump or carry a jerry can.
        private void UpdatePrepaidAutoFill()
        {
            if (_prepaidLitres <= 0.05f) return;
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) return;
            Vehicle veh = playerPed.CurrentVehicle;
            if (veh == null || veh.Driver != playerPed) return;
            if (_activeJob != ActiveJob.None) return;

            float pct = _maxFuel > 0 ? _fuel / _maxFuel : 1f;
            if (pct > 0.15f) return; // only kicks in once you're actually running low

            float drip = Math.Min(_prepaidLitres, 6f * Game.LastFrameTime); // ~6L/s auto top-up
            float space = Math.Max(0f, _maxFuel - _fuel);
            drip = Math.Min(drip, space);
            if (drip <= 0f) return;

            _fuel += drip;
            _prepaidLitres -= drip;
        }

        private void StartJob(ActiveJob job, float cost, string label, float litres)
        {
            if (_money < cost) { ShowToast("~r~Your money is not enough."); return; }
            _money -= (int)Math.Round(cost);
            _activeJob = job;
            _jobTargetFuel = job == ActiveJob.Refuel ? Math.Min(_maxFuel, _fuel + litres) : 0f;
            Vehicle veh = Game.Player.Character.CurrentVehicle;
            _jobStartValue = job == ActiveJob.Refuel ? _fuel
                : job == ActiveJob.Repair && veh != null ? veh.EngineHealth
                : 0f;
            _serviceMenu.Visible = false;
            ShowToast($"~g~{label}...~w~ ${cost:F2}");
        }

        private void UpdateActiveJob()
        {
            if (_activeJob == ActiveJob.None) return;
            Ped playerPed = Game.Player.Character;
            Vehicle veh = playerPed.IsInVehicle() ? playerPed.CurrentVehicle : null;
            if (veh == null) { _activeJob = ActiveJob.None; return; }

            float jobPct = 0f;
            string jobLabel = "";
            Color jobColor = Color.LimeGreen;

            switch (_activeJob)
            {
                case ActiveJob.Refuel:
                    _fuel = Math.Min(_jobTargetFuel, _fuel + _refuelSpeed * Game.LastFrameTime);
                    jobPct = _jobTargetFuel > 0f ? Math.Min(1f, (_fuel - _jobStartValue) / Math.Max(0.01f, _jobTargetFuel - _jobStartValue)) : 1f;
                    jobLabel = "Refueling"; jobColor = Color.LimeGreen;
                    if (_fuel >= _jobTargetFuel - 0.05f) { _activeJob = ActiveJob.None; ShowToast("~g~Refuel complete!"); SaveAllData(); }
                    break;
                case ActiveJob.Repair:
                    veh.EngineHealth = Math.Min(1000f, veh.EngineHealth + 150f * Game.LastFrameTime);
                    jobPct = Math.Min(1f, (veh.EngineHealth - _jobStartValue) / Math.Max(0.01f, 1000f - _jobStartValue));
                    jobLabel = "Repairing vehicle"; jobColor = Color.Cyan;
                    if (veh.EngineHealth >= 999.5f) { veh.Repair(); _activeJob = ActiveJob.None; ShowToast("~g~Vehicle repaired!"); }
                    break;
            }

            if (_activeJob != ActiveJob.None)
            {
                float barX = 20f, barY = 90f;
                new ContainerElement(new PointF(barX, barY), new SizeF(300, 30), Color.FromArgb(200, 20, 20, 20)).Draw();
                new ContainerElement(new PointF(barX + 5, barY + 20), new SizeF(290, 6), Color.FromArgb(150, 60, 60, 60)).Draw();
                new ContainerElement(new PointF(barX + 5, barY + 20), new SizeF(290 * jobPct, 6), jobColor).Draw();
                new TextElement($"{jobLabel}... {(int)(jobPct * 100)}%", new PointF(barX + 5, barY + 2), 0.24f, Color.White, Font.ChaletLondon).Draw();
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
        // FUEL DELIVERY BY PLANE
        // =================================================================
        private Vector3 _deliveryStartPos;
        private Vector3 _deliveryTargetPos;
        private float _deliveryFlightT;
        private const float DELIVERY_FLIGHT_SECONDS = 28f;

        private const float HOVER_DURATION_SECONDS = 12f;
        private const float HOSE_EXTEND_SECONDS = 1.5f;
        private bool _hoseAttachRight = true;

        // Finds a real rear-side tire position on the vehicle (left or
        // right, picked once per delivery) using the model's actual bounding
        // dimensions, transformed into world space - so the hose always
        // plugs in near an actual wheel instead of the vehicle's center.
        private Vector3 GetRearTireAttachPoint(Vehicle veh)
        {
            return veh.GetOffsetPosition(GetRearTireLocalOffset(veh));
        }

        // Same tire spot, but as a LOCAL (vehicle-relative) offset - this is
        // what ATTACH_ENTITIES_TO_ROPE actually wants for the target entity.
        private Vector3 GetRearTireLocalOffset(Vehicle veh)
        {
            var minOut = new OutputArgument();
            var maxOut = new OutputArgument();
            Function.Call(Hash.GET_MODEL_DIMENSIONS, veh.Model.Hash, minOut, maxOut);
            Vector3 min = minOut.GetResult<Vector3>();
            Vector3 max = maxOut.GetResult<Vector3>();
            float sideOffset = _hoseAttachRight ? max.X * 0.9f : min.X * 0.9f;
            return new Vector3(sideOffset, min.Y * 0.85f, min.Z + 0.15f);
        }

        // =================================================================
        // REAL NATIVE ROPE - the hose is a genuine GTA rope object attached
        // to the helicopter and the vehicle's rear tire, simulated and
        // rendered by the game itself every frame. It naturally stays
        // connected and sways as either entity moves, instead of a redrawn
        // 2D line that lags behind.
        // =================================================================
        private int _hoseRopeHandle = -1;
        private bool _ropeTexturesRequested = false;

        private void EnsureHoseRope(Vehicle heli, Vehicle targetVeh)
        {
            if (!_ropeTexturesRequested)
            {
                Function.Call(Hash.ROPE_LOAD_TEXTURES);
                _ropeTexturesRequested = true;
            }
            if (_hoseRopeHandle != -1) return; // already attached for this hover
            if (!Function.Call<bool>(Hash.ROPE_ARE_TEXTURES_LOADED)) return; // wait a frame or two for load

            Vector3 start = heli.Position;
            _hoseRopeHandle = Function.Call<int>(Hash.ADD_ROPE,
                start.X, start.Y, start.Z, 0f, 0f, 0f,
                6.0f,   // initial length
                3,      // rope type (thin cable)
                15.0f,  // max length
                0.2f,   // min length
                1.0f,   // length change rate
                false, false, false, true, 1.0f);

            Vector3 tireLocal = GetRearTireLocalOffset(targetVeh);
            Function.Call(Hash.ATTACH_ENTITIES_TO_ROPE, _hoseRopeHandle, heli, targetVeh,
                0f, 0f, -2f,
                tireLocal.X, tireLocal.Y, tireLocal.Z,
                12.0f, false, false, "", "");
        }

        private void DeleteHoseRope()
        {
            if (_hoseRopeHandle == -1) return;
            var ropeArg = new OutputArgument(_hoseRopeHandle);
            Function.Call(Hash.DELETE_ROPE, ropeArg);
            _hoseRopeHandle = -1;
        }
        private float _hoseExtendT = 0f;
        // City buildings top out well under 200m (Maze Bank Tower is the
        // tallest at ~200m), while GTA's mountains (Chiliad, Tongva, the
        // Vinewood hills) sit at real elevated Z values themselves. Basing
        // the choice on the player's own world-Z is a simple, reliable way
        // to tell "up a mountain" from "downtown" without terrain sampling.
        private const float CITY_CRUISE_ALTITUDE = 140f;
        private const float MOUNTAIN_CRUISE_ALTITUDE = 260f;
        private const float MOUNTAIN_ELEVATION_THRESHOLD = 70f;
        private float GetCruiseAltitude(Vector3 nearPos)
        {
            return nearPos.Z > MOUNTAIN_ELEVATION_THRESHOLD ? MOUNTAIN_CRUISE_ALTITUDE : CITY_CRUISE_ALTITUDE;
        }

        private void TryCallDelivery()
        {
            if (_deliveryPhase != DeliveryPhase.None) { ShowToast("~y~A delivery is already on its way."); return; }
            if (_money < _deliveryCost) { ShowToast($"~r~Not enough money (${_deliveryCost})."); return; }

            Ped playerPed = Game.Player.Character;

            _deliveryTargetPos = playerPed.Position + new Vector3(0, 0, 22f); // hover height above player
            _deliveryStartPos = playerPed.Position + new Vector3(0, 0, GetCruiseAltitude(playerPed.Position)) + (playerPed.ForwardVector * -500f);

            _deliveryPlane = World.CreateVehicle(VehicleHash.Maverick, _deliveryStartPos, 0f);
            if (_deliveryPlane == null) { ShowToast("~r~Delivery failed to launch."); return; }
            _deliveryPlane.IsPersistent = true;
            _deliveryPlane.IsPositionFrozen = true; // we drive this one by hand, not the physics/AI
            MovePlaneTo(_deliveryStartPos, faceDown: true); // face the target immediately instead of spawning sideways-on

            _deliveryPilot = World.CreatePed(PedHash.Pilot01SMM, _deliveryStartPos);
            if (_deliveryPilot != null)
            {
                _deliveryPilot.IsPersistent = true;
                _deliveryPilot.SetIntoVehicle(_deliveryPlane, VehicleSeat.Driver);
            }

            _deliveryBlip = _deliveryPlane.AddBlip();
            _deliveryBlip.Sprite = BlipSprite.Helicopter;
            _deliveryBlip.Color = BlipColor.Yellow;
            _deliveryBlip.Name = "Fuel Delivery";
            _deliveryBlip.ShowRoute = true;

            _money -= _deliveryCost;
            _deliveryPhase = DeliveryPhase.Inbound;
            _hoseAttachRight = Rand.Next(2) == 0;
            _deliveryFlightT = 0f;
            _deliveryFillProgress = 0f;
            ShowToast($"~g~Fuel delivery dispatched~w~ (${_deliveryCost}). Watch the sky above you.");
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

            switch (_deliveryPhase)
            {
                case DeliveryPhase.Inbound:
                    {
                        // BUG FIX: this used to lerp straight from the spawn
                        // point to the low ~22m hover height the whole way,
                        // so on a straight-line path across mountainous
                        // terrain the heli could clip INTO a ridge/peak
                        // partway there and be invisible even though it was
                        // technically still "flying". Now it cruises at a
                        // safe high altitude (well above typical terrain)
                        // during the approach, and only descends to hover
                        // height once directly above the player.
                        Vector3 cruiseTarget = playerPed.Position + new Vector3(0, 0, GetCruiseAltitude(playerPed.Position));
                        _deliveryTargetPos = playerPed.Position + new Vector3(0, 0, 22f);

                        _deliveryFlightT += Game.LastFrameTime / DELIVERY_FLIGHT_SECONDS;
                        float t = Math.Min(1f, _deliveryFlightT);
                        Vector3 pos = Vector3.Lerp(_deliveryStartPos, cruiseTarget, t);
                        MovePlaneTo(pos);

                        float dist = _deliveryPlane.Position.DistanceTo(playerPed.Position);
                        new TextElement($"Fuel delivery inbound: {dist:F0}m", new PointF(20, 60), 0.26f, Color.FromArgb(255, 255, 220, 120), Font.ChaletLondon).Draw();

                        if (t >= 1f)
                        {
                            _deliveryPhase = DeliveryPhase.Descending;
                            _deliveryFlightT = 0f;
                        }
                        break;
                    }

                case DeliveryPhase.Descending:
                    {
                        _deliveryFlightT += Game.LastFrameTime / 3.5f;
                        float t = Math.Min(1f, _deliveryFlightT);
                        Vector3 from = playerPed.Position + new Vector3(0, 0, GetCruiseAltitude(playerPed.Position));
                        Vector3 to = playerPed.Position + new Vector3(0, 0, 22f);
                        MovePlaneTo(Vector3.Lerp(from, to, t), faceDown: true);

                        new TextElement("Fuel delivery descending...", new PointF(20, 60), 0.26f, Color.FromArgb(255, 255, 220, 120), Font.ChaletLondon).Draw();

                        if (t >= 1f)
                        {
                            _deliveryPhase = DeliveryPhase.Hovering;
                            _deliveryFillProgress = 0f;
                            _hoseExtendT = 0f;
                        }
                        break;
                    }

                case DeliveryPhase.Hovering:
                    {
                        // stay locked above the player, facing down toward them
                        Vector3 hoverPos = playerPed.Position + new Vector3(0, 0, 22f);
                        MovePlaneTo(hoverPos, faceDown: true);

                        bool inVeh = playerPed.IsInVehicle();
                        Vehicle targetVeh = inVeh ? playerPed.CurrentVehicle : null;

                        if (inVeh)
                        {
                            // REAL native rope, physically attached to the
                            // heli and the vehicle's rear tire - GTA
                            // simulates and renders this every frame on its
                            // own, so it genuinely stays connected as either
                            // one moves, instead of a redrawn 2D line that
                            // visibly lags a frame behind.
                            EnsureHoseRope(_deliveryPlane, targetVeh);
                        }
                        else
                        {
                            // on-foot fallback: no vehicle to attach a real
                            // rope to, use the old screen-space hose instead
                            DeleteHoseRope();
                            Vector3 hoseAnchor = hoverPos + new Vector3(0, 0, -3f);
                            Vector3 hoseTargetEnd = playerPed.Position + new Vector3(0, 0, 0.3f);
                            Vector3 hoseEnd = Vector3.Lerp(hoseAnchor, hoseTargetEnd, _hoseExtendT);
                            DrawFuelHose(hoseAnchor, hoseEnd, Color.FromArgb(235, 220, 170, 40));
                        }

                        // hose "extending" still gates when the fill
                        // actually starts, even though the rope itself is
                        // already attached - gives a beat of lowering time
                        // before fuel starts flowing.
                        _hoseExtendT = Math.Min(1f, _hoseExtendT + Game.LastFrameTime / HOSE_EXTEND_SECONDS);
                        bool attached = _hoseExtendT >= 1f;
                        if (attached)
                        {
                            _deliveryFillProgress += Game.LastFrameTime / HOVER_DURATION_SECONDS;
                        }
                        float pct = Math.Min(1f, _deliveryFillProgress);
                        _fuel = Math.Min(_maxFuel, _maxFuel * pct);

                        // on-screen fill progress bar
                        float barX = 20f, barY = 90f;
                        new ContainerElement(new PointF(barX, barY), new SizeF(300, 30), Color.FromArgb(200, 20, 20, 20)).Draw();
                        new ContainerElement(new PointF(barX + 5, barY + 20), new SizeF(290, 6), Color.FromArgb(150, 60, 60, 60)).Draw();
                        new ContainerElement(new PointF(barX + 5, barY + 20), new SizeF(290 * (attached ? pct : _hoseExtendT), 6), Color.FromArgb(255, 255, 200, 60)).Draw();
                        string label = attached ? $"Fuel delivery - filling tank... {(int)(pct * 100)}%" : "Fuel delivery - lowering hose...";
                        new TextElement(label, new PointF(barX + 5, barY + 2), 0.24f, Color.White, Font.ChaletLondon).Draw();

                        if (_deliveryFillProgress >= 1f)
                        {
                            ShowToast("~g~Tank filled by air delivery!");
                            SaveAllData();
                            DeleteHoseRope();
                            _deliveryPhase = DeliveryPhase.Leaving;
                            _deliveryFlightT = 0f;
                        }
                        break;
                    }

                case DeliveryPhase.Leaving:
                    // flies back out the way it came in, then despawns
                    _deliveryFlightT += Game.LastFrameTime / 18f;
                    Vector3 leavePos = Vector3.Lerp(_deliveryTargetPos, _deliveryStartPos, Math.Min(1f, _deliveryFlightT));
                    MovePlaneTo(leavePos);
                    if (_deliveryFlightT >= 1f) CleanupDelivery();
                    break;
            }
        }

        // Projects a world point to screen space (0-1 normalized -> pixels).
        private bool WorldToScreen(Vector3 world, out float screenX, out float screenY)
        {
            var ox = new OutputArgument();
            var oy = new OutputArgument();
            bool onScreen = Function.Call<bool>(Hash.GET_SCREEN_COORD_FROM_WORLD_COORD, world.X, world.Y, world.Z, ox, oy);
            screenX = onScreen ? ox.GetResult<float>() * Screen.Width : 0f;
            screenY = onScreen ? oy.GetResult<float>() * Screen.Height : 0f;
            return onScreen;
        }

        // Draws a dotted "hose/pipe" line on screen between two world points -
        // used to show fuel visibly flowing from the heli down to the player.
        // BUG FIX: this used to require BOTH endpoints to be on-screen, so
        // the hose disappeared whenever the heli (hovering well above, often
        // out of the driving camera's view) wasn't in frame. Now if only the
        // heli end is off-screen, the hose anchors from the top edge instead
        // of vanishing entirely.
        private void DrawFuelHose(Vector3 fromWorld, Vector3 toWorld, Color color)
        {
            bool toOk = WorldToScreen(toWorld, out float x2, out float y2);
            if (!toOk) return; // nothing to anchor to if the attach point itself isn't visible

            bool fromOk = WorldToScreen(fromWorld, out float x1, out float y1);
            if (!fromOk) { x1 = x2; y1 = 0f; } // heli off top of frame - hose comes down from the top edge

            // Dense, overlapping segments (no gaps) with a slight sideways
            // sag partway down reads as a solid hanging rope/pipe instead of
            // a spaced-out dotted line.
            const int SEGMENTS = 40;
            float dx = x2 - x1, dy = y2 - y1;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            float sagAmount = Math.Min(18f, len * 0.06f);
            Color coreColor = Color.FromArgb(color.A, Math.Max(0, color.R - 60), Math.Max(0, color.G - 60), Math.Max(0, color.B - 60));

            for (int i = 0; i <= SEGMENTS; i++)
            {
                float t = i / (float)SEGMENTS;
                float sag = (float)Math.Sin(t * Math.PI) * sagAmount; // bows outward at the midpoint like real rope slack
                float px = x1 + dx * t + sag;
                float py = y1 + dy * t;
                new ContainerElement(new PointF(px - 4, py - 4), new SizeF(8, 8), color).Draw();
                new ContainerElement(new PointF(px - 2, py - 2), new SizeF(4, 4), coreColor).Draw();
            }
        }

        // Moves the delivery plane by hand instead of relying on an AI flight
        // task - guarantees it never clips a building or gets stuck on terrain,
        // since it's just following a straight scripted line through open air.
        // faceDown=true is used while hovering so it visibly noses toward the
        // player/hose target instead of staring off sideways.
        private void MovePlaneTo(Vector3 pos, bool faceDown = false)
        {
            Vector3 dir = faceDown ? (_deliveryTargetPos - pos) : (pos - _deliveryPlane.Position);
            if (dir.Length() > 0.01f)
            {
                // NOTE: GTA heading 0 = facing +Y (north), and increases
                // clockwise toward +X. Forward vector relation is
                // forward.X = -sin(heading), forward.Y = cos(heading), so the
                // correct inverse is atan2(-dir.X, dir.Y). Previous formula
                // was mirrored on X (atan2(dir.X, dir.Y)), which is why the
                // heli looked like it was facing sideways while approaching.
                float heading = (float)(Math.Atan2(-dir.X, dir.Y) * (180.0 / Math.PI));
                if (heading < 0) heading += 360f;
                _deliveryPlane.Heading = heading;
            }
            _deliveryPlane.Position = pos;
        }

        private void CleanupDelivery()
        {
            DeleteHoseRope();
            if (_deliveryBlip != null && _deliveryBlip.Exists()) _deliveryBlip.Delete();
            if (_deliveryPilot != null && _deliveryPilot.Exists()) _deliveryPilot.Delete();
            if (_deliveryPlane != null && _deliveryPlane.Exists()) _deliveryPlane.Delete();
            _deliveryBlip = null; _deliveryPilot = null; _deliveryPlane = null;
            _deliveryPhase = DeliveryPhase.None;
        }

        // =================================================================
        // HUD - one combined box: main fuel bar (with live % readout at the
        // top of it) plus a second vertical usage line next to it that
        // rises/falls with how much fuel the vehicle is currently burning -
        // near-empty when stopped/idling, near-full at max consumption.
        // =================================================================
        private void UpdateHud()
        {
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle() && !_hudMoveMode) return; // no HUD on foot, unless actively positioning it
            Vehicle veh = playerPed.IsInVehicle() ? playerPed.CurrentVehicle : null;
            if (!_hudMoveMode && (veh == null || !veh.Exists())) return;

            float tankHeight = 200f * _hudScale;
            float tankTop = Screen.Height + _hudOffsetY - tankHeight; // _hudOffsetY is negative
            float tankW = 34f * _hudScale;
            float tankGap = 14f * _hudScale;
            float labelH = 18f * _hudScale;

            float panelX = _hudOffsetX;
            bool showThrust = veh != null && veh.ClassType == VehicleClass.Planes;
            float panelW = tankW * (showThrust ? 3f : 2f) + tankGap * (showThrust ? 2f : 1f);
            // shared box for both/all tanks, with room above for % and below for labels
            new ContainerElement(new PointF(panelX - 8, tankTop - 28), new SizeF(panelW + 16, tankHeight + 28 + labelH + 10), Color.FromArgb(160, 0, 0, 0)).Draw();

            // ---- FUEL TANK ----
            float fuelPct = _maxFuel > 0 ? _fuel / _maxFuel : 0f;
            _displayedFuelPct += (fuelPct - _displayedFuelPct) * Math.Min(1f, Game.LastFrameTime * 2.5f);
            bool fuelLow = fuelPct < 0.2f;
            DrawFuelTank(panelX, tankTop, tankW, tankHeight, _displayedFuelPct, Color.LimeGreen, Color.Red, fuelLow);

            string fuelPctText = $"{(int)Math.Round(_displayedFuelPct * 100f)}%";
            new TextElement(fuelPctText, new PointF(panelX + tankW / 2f - 12f, tankTop - 24f), 0.26f,
                fuelLow ? Color.Red : Color.White, Font.ChaletLondon).Draw();
            new TextElement("FUEL", new PointF(panelX + tankW / 2f - 16f, tankTop + tankHeight + 6f), 0.22f, Color.White, Font.ChaletLondon).Draw();

            // ---- CONSUMPTION TANK ----
            // Reference ceiling = the highest possible burn rate at the
            // current consume-rate setting, so it reads empty when stopped
            // and fills toward full under max engine load/speed.
            float maxLps = (6.5f / 100f) * 2.6f * Math.Max(0.1f, _fuelConsumeRate);
            float usagePct = maxLps > 0f ? Math.Min(1f, _currentUsageLps / maxLps) : 0f;
            _displayedUsagePct += (usagePct - _displayedUsagePct) * Math.Min(1f, Game.LastFrameTime * 4f);

            float usageX = panelX + tankW + tankGap;
            Color usageColor = Color.FromArgb(255, 255, 180, 40);
            DrawFuelTank(usageX, tankTop, tankW, tankHeight, _displayedUsagePct, usageColor, usageColor, false);

            string usagePctText = $"{(int)Math.Round(_displayedUsagePct * 100f)}%";
            new TextElement(usagePctText, new PointF(usageX + tankW / 2f - 12f, tankTop - 24f), 0.26f, Color.White, Font.ChaletLondon).Draw();
            new TextElement("USE", new PointF(usageX + tankW / 2f - 12f, tankTop + tankHeight + 6f), 0.22f, Color.White, Font.ChaletLondon).Draw();

            // ---- JET THRUST TANK (jets only) ----
            if (showThrust)
            {
                float thrustPct = Math.Max(veh.CurrentRPM, Game.GetControlValueNormalized(GTA.Control.VehicleAccelerate));
                _displayedThrustPct += (thrustPct - _displayedThrustPct) * Math.Min(1f, Game.LastFrameTime * 5f);

                float thrustX = usageX + tankW + tankGap;
                Color thrustColor = Color.Cyan;
                DrawFuelTank(thrustX, tankTop, tankW, tankHeight, _displayedThrustPct, thrustColor, thrustColor, false);

                string thrustPctText = $"{(int)Math.Round(_displayedThrustPct * 100f)}%";
                new TextElement(thrustPctText, new PointF(thrustX + tankW / 2f - 12f, tankTop - 24f), 0.26f, Color.White, Font.ChaletLondon).Draw();
                new TextElement("THR", new PointF(thrustX + tankW / 2f - 12f, tankTop + tankHeight + 6f), 0.22f, Color.White, Font.ChaletLondon).Draw();
            }

            if (_hudMoveMode)
            {
                Color hi = Color.FromArgb(255, 60, 220, 255);
                float hx = panelX - 8, hy = tankTop - 28, hw = panelW + 16, hh = tankHeight + 28 + labelH + 10;
                float bw = 3f;
                new ContainerElement(new PointF(hx, hy), new SizeF(hw, bw), hi).Draw();
                new ContainerElement(new PointF(hx, hy + hh - bw), new SizeF(hw, bw), hi).Draw();
                new ContainerElement(new PointF(hx, hy), new SizeF(bw, hh), hi).Draw();
                new ContainerElement(new PointF(hx + hw - bw, hy), new SizeF(bw, hh), hi).Draw();
                new TextElement("Arrows: move | +/-: resize | Enter: save | Backspace: cancel",
                    new PointF(hx, hy + hh + 8f), 0.24f, hi, Font.ChaletLondon).Draw();
            }
        }

        // Draws a single tank-shaped gauge: an outlined box with one solid
        // liquid fill level rising from the bottom (like a real fuel tank),
        // instead of blocky segments.
        private void DrawFuelTank(float x, float top, float w, float h, float pct, Color normalColor, Color lowColor, bool isLow)
        {
            pct = Math.Max(0f, Math.Min(1f, pct));
            Color fillColor = isLow ? lowColor : normalColor;

            // tank outline/background
            new ContainerElement(new PointF(x, top), new SizeF(w, h), Color.FromArgb(150, 30, 30, 30)).Draw();

            // liquid fill, bottom-up
            float fillH = h * pct;
            new ContainerElement(new PointF(x, top + (h - fillH)), new SizeF(w, fillH), fillColor).Draw();

            // thin border so it reads as a container, not just a colored block
            float bw = 2f;
            Color border = Color.FromArgb(200, 10, 10, 10);
            new ContainerElement(new PointF(x, top), new SizeF(w, bw), border).Draw();
            new ContainerElement(new PointF(x, top + h - bw), new SizeF(w, bw), border).Draw();
            new ContainerElement(new PointF(x, top), new SizeF(bw, h), border).Draw();
            new ContainerElement(new PointF(x + w - bw, top), new SizeF(bw, h), border).Draw();
        }

        // =================================================================
        // AIRCRAFT THREAT RADAR - enemy fighter jet/heli detector + guided
        // missile lock warning, active only while flying a plane/helicopter.
        // Uses REAL native GTA Blips attached to each hostile aircraft, so
        // they render through the game's own minimap/main map system - no
        // custom-drawn overlay, box, or separate radar panel at all.
        //
        // Missile-lock detection now reads the aircraft's own native homing
        // weapon state (GET_VEHICLE_HOMING_LOCKON_STATE) - if IT has an
        // active lock, that's a real lock, not just a distance/angle guess.
        // The angle/range check is kept as a fallback for aircraft without
        // homing weapons (cannon-only fighters) so they still register as a
        // threat when lined up on you.
        // =================================================================
        private const float THREAT_SCAN_RADIUS = 2200f;
        private const float CIVILIAN_SHOW_RADIUS = 350f; // non-hostile aircraft only show up this close
        private const float MISSILE_LOCK_RANGE = 600f;
        private const float MISSILE_LOCK_CONE_DOT = 0.80f; // ~37 degrees, wide catch cone
        private DateTime _lastThreatBeep = DateTime.MinValue;
        private readonly Dictionary<int, Blip> _threatBlips = new Dictionary<int, Blip>();
        private readonly Dictionary<int, DateTime> _flareDetrackedUntil = new Dictionary<int, DateTime>();

        private void UpdateAircraftThreatRadar()
        {
            Ped playerPed = Game.Player.Character;
            bool active = playerPed.IsInVehicle() &&
                (playerPed.CurrentVehicle.ClassType == VehicleClass.Planes || playerPed.CurrentVehicle.ClassType == VehicleClass.Helicopters);
            Vehicle myVeh = active ? playerPed.CurrentVehicle : null;

            UpdatePlayerMissiles(playerPed, active, myVeh);
            UpdateEnemyMissiles(playerPed, active, myVeh);
            UpdateFlares(playerPed, active, myVeh);

            if (!active)
            {
                ClearThreatBlips();
                return;
            }

            var seenHandles = new HashSet<int>();
            bool anyLock = false;
            bool anyAiming = false;

            foreach (Vehicle v in World.GetNearbyVehicles(myVeh.Position, THREAT_SCAN_RADIUS))
            {
                if (v == null || !v.Exists() || v == myVeh) continue;
                if (v.ClassType != VehicleClass.Planes && v.ClassType != VehicleClass.Helicopters) continue;
                Ped driver = v.Driver;
                if (driver == null || !driver.Exists() || driver.IsDead) continue;

                float dist = v.Position.DistanceTo(myVeh.Position);

                // BUG FIX: the relationship-hate check was flagging nearly
                // every aircraft as hostile (default civilian relationship
                // groups apparently weren't reading as expected), which is
                // why every plane showed red. Hostility now comes ONLY from
                // an actual combat flag against the player, or a confirmed
                // military response vehicle model at high wanted level -
                // both far more reliable signals.
                bool inCombat = Function.Call<bool>(Hash.IS_PED_IN_COMBAT, driver, playerPed);
                VehicleHash vh = (VehicleHash)v.Model.Hash;
                bool isMilitaryModel = vh == VehicleHash.Hunter || vh == VehicleHash.Lazer || vh == VehicleHash.Besra || vh == VehicleHash.Savage;
                bool hostile = inCombat || (isMilitaryModel && Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player) >= 3);

                if (!hostile)
                {
                    // Not a threat - only show it at all (green, informational)
                    // once it's genuinely close, and it never contributes to
                    // lock/aiming warnings.
                    if (dist <= CIVILIAN_SHOW_RADIUS)
                    {
                        seenHandles.Add(v.Handle);
                        SyncThreatBlip(v, locking: false, hostile: false);
                    }
                    continue;
                }

                // real native homing-lock state on the ENEMY aircraft itself -
                // this is the ONLY thing that turns the blip orange, since
                // it's a real lock. Non-homing/cannon aircraft can't produce
                // this state, so they correctly never show as "locked".
                int lockState = Function.Call<int>(Hash.GET_VEHICLE_HOMING_LOCKON_STATE, v);
                bool nativeLocked = lockState >= 2;

                Vector3 toMe = (myVeh.Position - v.Position).Normalized;
                float aim = Vector3.Dot(v.ForwardVector, toMe);
                // fallback for cannon-only aircraft: lined up and close =
                // genuine danger, but NOT a missile lock, so it stays red
                // and triggers a separate "INCOMING FIRE" warning instead of
                // the orange missile-lock indicator.
                bool aimingAtMe = dist <= MISSILE_LOCK_RANGE && aim >= MISSILE_LOCK_CONE_DOT;

                bool locking = nativeLocked;

                // flares suppress a real lock for a few seconds even if it
                // re-acquires immediately
                if (_flareDetrackedUntil.TryGetValue(v.Handle, out DateTime until) && DateTime.Now < until)
                    locking = false;

                if (locking) anyLock = true;
                if (aimingAtMe && !locking) anyAiming = true;

                seenHandles.Add(v.Handle);
                SyncThreatBlip(v, locking, hostile: true);
            }

            // remove blips for vehicles no longer a threat or no longer nearby
            foreach (var handle in new List<int>(_threatBlips.Keys))
            {
                if (seenHandles.Contains(handle)) continue;
                if (_threatBlips[handle] != null && _threatBlips[handle].Exists()) _threatBlips[handle].Delete();
                _threatBlips.Remove(handle);
            }

            if (anyLock)
            {
                new TextElement("MISSILE LOCK WARNING", new PointF(Screen.Width / 2f - 140, 40), 0.5f, Color.FromArgb(255, 255, 140, 0), Font.ChaletComprimeCologne, GTA.UI.Alignment.Left, true, true).Draw();
                if ((DateTime.Now - _lastThreatBeep).TotalMilliseconds > 700)
                {
                    _lastThreatBeep = DateTime.Now;
                    Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "5_SEC_WARNING", "MP_LEADERBOARD_SOUNDSET", false);
                }
            }
            else if (anyAiming)
            {
                new TextElement("INCOMING FIRE", new PointF(Screen.Width / 2f - 100, 40), 0.45f, Color.Red, Font.ChaletComprimeCologne, GTA.UI.Alignment.Left, true, true).Draw();
            }
        }

        // Creates/updates a real native Blip pinned to the aircraft itself -
        // GTA moves it on the minimap/main map automatically every frame.
        // Icon matches the aircraft type (Jet vs Helicopter). Color: Green =
        // nearby but not a threat, Red = hostile, Orange+flashing = real
        // missile lock.
        private void SyncThreatBlip(Vehicle v, bool locking, bool hostile)
        {
            if (!_threatBlips.TryGetValue(v.Handle, out Blip b) || b == null || !b.Exists())
            {
                b = v.AddBlip();
                b.Sprite = v.ClassType == VehicleClass.Helicopters ? BlipSprite.Helicopter : BlipSprite.Jet;
                b.Scale = 0.9f;
                _threatBlips[v.Handle] = b;
            }
            b.Name = !hostile ? "Aircraft" : (v.ClassType == VehicleClass.Helicopters ? "Hostile Helicopter" : "Hostile Jet");
            b.Color = !hostile ? BlipColor.Green : (locking ? BlipColor.Orange : BlipColor.Red);
            b.IsFlashing = locking;
        }

        private void ClearThreatBlips()
        {
            if (_threatBlips.Count == 0) return;
            foreach (var b in _threatBlips.Values)
                if (b != null && b.Exists()) b.Delete();
            _threatBlips.Clear();
        }

        // =================================================================
        // PLAYER-FIRED MISSILE TRACKING - shows YOUR missile on the main
        // map too, using a real native Blip (no overlay/box). SHVDN doesn't
        // expose the raw in-flight projectile entity, so the blip is
        // advanced manually - but it actively STEERS toward the nearest
        // locked hostile target every tick (proper homing curve), falling
        // back to a straight flight path only if nothing is locked.
        // =================================================================
        private class PlayerMissile { public Vector3 Position; public Vector3 Direction; public float LifeLeft; public Blip Blip; public int TargetHandle; }
        private readonly List<PlayerMissile> _playerMissiles = new List<PlayerMissile>();
        private bool _wasShootingLastTick = false;
        private const float PLAYER_MISSILE_SPEED = 280f; // m/s
        private const float PLAYER_MISSILE_LIFETIME = 6f;
        private const float PLAYER_MISSILE_TURN_RATE = 3.5f; // radians/sec steering toward target

        private void UpdatePlayerMissiles(Ped playerPed, bool active, Vehicle myVeh)
        {
            if (active)
            {
                bool isShooting = Function.Call<bool>(Hash.IS_PED_SHOOTING, playerPed);
                if (isShooting && !_wasShootingLastTick)
                {
                    // lock onto the nearest currently-tracked hostile threat, if any
                    int targetHandle = 0;
                    float bestDist = float.MaxValue;
                    foreach (var handle in _threatBlips.Keys)
                    {
                        var ent = Entity.FromHandle(handle);
                        if (ent == null || !ent.Exists()) continue;
                        float d = ent.Position.DistanceTo(myVeh.Position);
                        if (d < bestDist) { bestDist = d; targetHandle = handle; }
                    }

                    var m = new PlayerMissile
                    {
                        Position = myVeh.Position + myVeh.ForwardVector * 6f,
                        Direction = myVeh.ForwardVector,
                        LifeLeft = PLAYER_MISSILE_LIFETIME,
                        TargetHandle = targetHandle
                    };
                    m.Blip = World.CreateBlip(m.Position);
                    m.Blip.Sprite = BlipSprite.Standard;
                    m.Blip.Color = BlipColor.Blue;
                    m.Blip.Scale = 0.7f;
                    m.Blip.Name = targetHandle != 0 ? "Your Missile (Homing)" : "Your Missile";
                    m.Blip.IsFlashing = true;
                    _playerMissiles.Add(m);
                }
                _wasShootingLastTick = isShooting;
            }
            else
            {
                _wasShootingLastTick = false;
            }

            for (int i = _playerMissiles.Count - 1; i >= 0; i--)
            {
                var m = _playerMissiles[i];

                // steer toward the live target position each tick - this is
                // what actually makes it curve like a real guided missile
                // instead of just flying dead straight.
                Entity target = m.TargetHandle != 0 ? Entity.FromHandle(m.TargetHandle) : null;
                bool targetAlive = target != null && target.Exists();

                if (targetAlive)
                {
                    Vector3 toTarget = (target.Position - m.Position).Normalized;
                    float turnStep = PLAYER_MISSILE_TURN_RATE * Game.LastFrameTime;
                    m.Direction = Vector3.Lerp(m.Direction, toTarget, Math.Min(1f, turnStep)).Normalized;
                }

                m.Position += m.Direction * PLAYER_MISSILE_SPEED * Game.LastFrameTime;
                m.LifeLeft -= Game.LastFrameTime;
                if (m.Blip != null && m.Blip.Exists()) m.Blip.Position = m.Position;

                bool hitTarget = targetAlive && target.Position.DistanceTo(m.Position) < 15f;
                bool hitAnyThreat = !targetAlive && _threatBlips.Values.Any(b => b != null && b.Exists() && b.Position.DistanceTo(m.Position) < 25f);

                if (m.LifeLeft <= 0f || hitTarget || hitAnyThreat)
                {
                    if (m.Blip != null && m.Blip.Exists()) m.Blip.Delete();
                    _playerMissiles.RemoveAt(i);
                }
            }
        }

        private void ClearPlayerMissiles()
        {
            foreach (var m in _playerMissiles)
                if (m.Blip != null && m.Blip.Exists()) m.Blip.Delete();
            _playerMissiles.Clear();
        }

        // =================================================================
        // ENEMY MISSILE SIMULATION - GTA doesn't expose enemy-fired
        // projectile entities either, so once a hostile aircraft's native
        // homing lock (GET_VEHICLE_HOMING_LOCKON_STATE) has held for ~1.2s
        // straight (long enough that a real pilot would have fired), a
        // simulated enemy missile is spawned that homes in on the player -
        // shown as a red blip on the main map, symmetric to "Your Missile".
        // =================================================================
        private class EnemyMissile { public Vector3 Position; public Vector3 Direction; public float LifeLeft; public Blip Blip; public int SourceHandle; }
        private readonly List<EnemyMissile> _enemyMissiles = new List<EnemyMissile>();
        private readonly Dictionary<int, float> _lockHoldSeconds = new Dictionary<int, float>();
        private readonly HashSet<int> _alreadyFiredAt = new HashSet<int>();
        private const float ENEMY_FIRE_LOCK_HOLD = 1.2f;
        private const float ENEMY_MISSILE_SPEED = 260f;
        private const float ENEMY_MISSILE_LIFETIME = 7f;
        private const float ENEMY_MISSILE_TURN_RATE = 3.0f;

        private void UpdateEnemyMissiles(Ped playerPed, bool active, Vehicle myVeh)
        {
            if (active)
            {
                foreach (var kvp in _threatBlips)
                {
                    int handle = kvp.Key;
                    bool locking = kvp.Value != null && kvp.Value.Exists() && kvp.Value.Color == BlipColor.Orange;

                    if (locking)
                    {
                        _lockHoldSeconds.TryGetValue(handle, out float held);
                        held += Game.LastFrameTime;
                        _lockHoldSeconds[handle] = held;

                        if (held >= ENEMY_FIRE_LOCK_HOLD && !_alreadyFiredAt.Contains(handle))
                        {
                            var src = Entity.FromHandle(handle);
                            if (src != null && src.Exists())
                            {
                                var em = new EnemyMissile
                                {
                                    Position = src.Position,
                                    Direction = (myVeh.Position - src.Position).Normalized,
                                    LifeLeft = ENEMY_MISSILE_LIFETIME,
                                    SourceHandle = handle
                                };
                                em.Blip = World.CreateBlip(em.Position);
                                em.Blip.Sprite = BlipSprite.Standard;
                                em.Blip.Color = BlipColor.Red;
                                em.Blip.Scale = 0.7f;
                                em.Blip.Name = "Enemy Missile";
                                em.Blip.IsFlashing = true;
                                _enemyMissiles.Add(em);
                                _alreadyFiredAt.Add(handle);
                            }
                        }
                    }
                    else
                    {
                        _lockHoldSeconds[handle] = 0f;
                        _alreadyFiredAt.Remove(handle);
                    }
                }
            }
            else
            {
                _lockHoldSeconds.Clear();
                _alreadyFiredAt.Clear();
            }

            for (int i = _enemyMissiles.Count - 1; i >= 0; i--)
            {
                var em = _enemyMissiles[i];

                if (active && myVeh != null)
                {
                    Vector3 toPlayer = (myVeh.Position - em.Position).Normalized;
                    float turnStep = ENEMY_MISSILE_TURN_RATE * Game.LastFrameTime;
                    em.Direction = Vector3.Lerp(em.Direction, toPlayer, Math.Min(1f, turnStep)).Normalized;
                }

                em.Position += em.Direction * ENEMY_MISSILE_SPEED * Game.LastFrameTime;
                em.LifeLeft -= Game.LastFrameTime;
                if (em.Blip != null && em.Blip.Exists()) em.Blip.Position = em.Position;

                bool hitPlayer = active && myVeh != null && myVeh.Position.DistanceTo(em.Position) < 15f;

                if (em.LifeLeft <= 0f || hitPlayer || !active)
                {
                    if (em.Blip != null && em.Blip.Exists()) em.Blip.Delete();
                    _enemyMissiles.RemoveAt(i);
                }
            }
        }

        private void ClearEnemyMissiles()
        {
            foreach (var em in _enemyMissiles)
                if (em.Blip != null && em.Blip.Exists()) em.Blip.Delete();
            _enemyMissiles.Clear();
        }

        // =================================================================
        // FLARES - countermeasure against enemy missile locks. Press Space
        // flying to pop a flare: shows as a real yellow blip drifting behind
        // the aircraft (native blip on the main map, no overlay), and rolls
        // a chance to break any currently-locking hostile's lock for a few
        // seconds (native lock state re-syncs on its own after; the
        // suppression window just stops it re-registering as a threat here).
        // =================================================================
        private class Flare { public Vector3 Position; public Vector3 Direction; public float LifeLeft; public Blip Blip; }
        private readonly List<Flare> _flares = new List<Flare>();
        private bool _wasFlareKeyDownLastTick = false;
        private const float FLARE_LIFETIME = 4f;
        private const float FLARE_DRIFT_SPEED = 25f;
        private const float FLARE_BREAK_CHANCE = 0.65f;
        private const float FLARE_SUPPRESS_SECONDS = 4f;

        private void FireFlare(Vehicle myVeh)
        {
            var f = new Flare
            {
                Position = myVeh.Position - myVeh.ForwardVector * 4f,
                Direction = -myVeh.ForwardVector,
                LifeLeft = FLARE_LIFETIME
            };
            f.Blip = World.CreateBlip(f.Position);
            f.Blip.Sprite = BlipSprite.Standard;
            f.Blip.Color = BlipColor.Yellow;
            f.Blip.Scale = 0.6f;
            f.Blip.Name = "Flare";
            f.Blip.IsFlashing = true;
            _flares.Add(f);

            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "COUNTDOWN", "HUD_MINI_GAME_SOUNDSET", false);

            // roll against every currently-locking threat
            foreach (var handle in _threatBlips.Keys)
            {
                var ent = Entity.FromHandle(handle);
                if (ent == null || !ent.Exists()) continue;
                bool wasLocking = _threatBlips[handle] != null && _threatBlips[handle].Color == BlipColor.Orange;
                if (!wasLocking) continue;
                if (Rand.NextDouble() < FLARE_BREAK_CHANCE)
                    _flareDetrackedUntil[handle] = DateTime.Now.AddSeconds(FLARE_SUPPRESS_SECONDS);
            }
        }

        private void UpdateFlares(Ped playerPed, bool active, Vehicle myVeh)
        {
            if (active)
            {
                bool keyDown = Game.IsKeyPressed(Keys.E);
                if (keyDown && !_wasFlareKeyDownLastTick) FireFlare(myVeh);
                _wasFlareKeyDownLastTick = keyDown;
            }
            else
            {
                _wasFlareKeyDownLastTick = false;
            }

            for (int i = _flares.Count - 1; i >= 0; i--)
            {
                var f = _flares[i];
                f.Position += f.Direction * FLARE_DRIFT_SPEED * Game.LastFrameTime;
                f.Position -= new Vector3(0, 0, 3f * Game.LastFrameTime); // gentle sink like a real flare
                f.LifeLeft -= Game.LastFrameTime;
                if (f.Blip != null && f.Blip.Exists()) f.Blip.Position = f.Position;

                if (f.LifeLeft <= 0f)
                {
                    if (f.Blip != null && f.Blip.Exists()) f.Blip.Delete();
                    _flares.RemoveAt(i);
                }
            }
        }

        private void ClearFlares()
        {
            foreach (var f in _flares)
                if (f.Blip != null && f.Blip.Exists()) f.Blip.Delete();
            _flares.Clear();
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
            ShowToast(_radioOn ? "~g~Radio on" : "~y~Radio off");
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
            var machine = station.Machines.Values.First().Position;
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
        // PERSISTENCE - all state (vehicle save data, config, HUD position)
        // now lives in ONE file instead of three, split by [SECTION] markers.
        // =================================================================
        private void LoadAllData()
        {
            try
            {
                if (!File.Exists(DataPath)) return;
                string section = "";
                foreach (var raw in File.ReadAllLines(DataPath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("[") && line.EndsWith("]")) { section = line; continue; }

                    switch (section)
                    {
                        case "[SAVE]":
                            {
                                var parts = line.Split('|');
                                if (parts.Length == 2 &&
                                    float.TryParse(parts[1], out float f))
                                {
                                    _savedByPlate[parts[0]] = f;
                                }
                                break;
                            }
                        case "[PREPAID]":
                            float.TryParse(line, out _prepaidLitres);
                            break;
                        case "[CONFIG]":
                            {
                                var parts = line.Split('|');
                                if (parts.Length >= 4)
                                {
                                    _fuelConsumeRate = float.Parse(parts[0]);
                                    _refuelSpeed = float.Parse(parts[1]);
                                    _priceRegular = float.Parse(parts[2]);
                                    _priceDiesel = float.Parse(parts[3]);
                                    if (parts.Length >= 5) _pricePremium = float.Parse(parts[4]);
                                    if (parts.Length >= 6) _deliveryCost = (int)float.Parse(parts[5]);
                                }
                                break;
                            }
                        case "[HUD]":
                            {
                                var parts = line.Split('|');
                                if (parts.Length >= 3)
                                {
                                    _hudOffsetX = float.Parse(parts[0]);
                                    _hudOffsetY = float.Parse(parts[1]);
                                    _hudScale = float.Parse(parts[2]);
                                }
                                break;
                            }
                    }
                }
            }
            catch (Exception ex) { Log("Load error: " + ex.Message); }
        }

        // Writes the whole combined file every time.
        private void SaveAllData()
        {
            try
            {
                Ped playerPed = Game.Player.Character;
                if (playerPed.IsInVehicle())
                {
                    string plate = Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, playerPed.CurrentVehicle)?.Trim();
                    if (!string.IsNullOrEmpty(plate))
                        _savedByPlate[plate] = _fuel;
                }

                var lines = new List<string> { "[SAVE]" };
                lines.AddRange(_savedByPlate.Select(kv => $"{kv.Key}|{kv.Value:F1}"));

                lines.Add("[PREPAID]");
                lines.Add(_prepaidLitres.ToString("F1"));

                lines.Add("[CONFIG]");
                lines.Add(string.Join("|",
                    _fuelConsumeRate.ToString("F2"), _refuelSpeed.ToString("F1"),
                    _priceRegular.ToString("F2"), _priceDiesel.ToString("F2"), _pricePremium.ToString("F2"),
                    _deliveryCost.ToString()));

                lines.Add("[HUD]");
                lines.Add($"{_hudOffsetX:F0}|{_hudOffsetY:F0}|{_hudScale:F1}");

                File.WriteAllLines(DataPath, lines);
            }
            catch (Exception ex) { Log("Save error: " + ex.Message); }
        }

        private void CheckVehicleSwitch()
        {
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle())
            {
                // Force a save the moment the player gets OUT of a vehicle -
                // previously this only happened on F7-close or job-complete,
                // so if neither occurred before the game/script restarted,
                // the exact fuel value was lost and the next entry fell back
                // to the 50% default. Exiting is the one moment we know for
                // sure the value needs to be committed.
                if (_lastVehicleHandle != -1) SaveAllData();
                _lastVehicleHandle = -1;
                return;
            }

            Vehicle veh = playerPed.CurrentVehicle;
            if (veh.Handle == _lastVehicleHandle) return;
            _lastVehicleHandle = veh.Handle;

            string plate = Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, veh)?.Trim();
            _maxFuel = GetTankSize(veh);

            if (!string.IsNullOrEmpty(plate) && _savedByPlate.TryGetValue(plate, out float saved))
            {
                _fuel = saved;
            }
            else
            {
                _fuel = _maxFuel * 0.5f;
            }

            _displayedFuelPct = _maxFuel > 0 ? _fuel / _maxFuel : 1f;
            _radioOn = true;
        }

        // =================================================================
        // KEY HANDLING - F7 (settings), X (radio), H (call fuel delivery).
        // Menus themselves are driven entirely by arrow keys + Enter via
        // LemonUI.
        // =================================================================
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (_hudMoveMode)
                {
                    HandleHudLiveMoveKey(e.KeyCode);
                    return;
                }

                if (e.KeyCode == Keys.F7)
                {
                    bool opening = !_settingsMenu.Visible;
                    _settingsMenu.Visible = opening;
                    if (!opening) { SaveAllData(); }
                    return;
                }

                if (_settingsMenu.Visible || _serviceMenu.Visible) return; // let LemonUI handle nav keys

                if (e.KeyCode == Keys.X) ToggleRadio();
                else if (e.KeyCode == Keys.H) TryCallDelivery();
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
