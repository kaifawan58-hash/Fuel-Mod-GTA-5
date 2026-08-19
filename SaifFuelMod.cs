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
//  - Fuel delivery: call it from the Settings menu, a helicopter flies in (tracked
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
        private const string DataPath = "scripts\\SaifFuelMod_data.txt";

        // HUD placement/scale - fully custom position, no preset corners
        private float _hudOffsetX = 285f;
        private float _hudOffsetY = -260f; // negative = measured up from bottom of screen
        private float _hudScale = 1.0f;
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
        private float _jobStartValue = 0f;

        // Siphoning - progress bar, fills the jerry can, not the vehicle
        private bool _theftInProgress = false;
        private float _theftProgress = 0f;
        private Vehicle _theftTarget = null;
        private const float THEFT_DURATION = 6f;

        // Fuel delivery by helicopter
        private enum DeliveryPhase { None, Inbound, Hovering, WaitingPickup, Leaving }
        private DeliveryPhase _deliveryPhase = DeliveryPhase.None;
        private Vehicle _deliveryPlane = null;
        private Ped _deliveryPilot = null;
        private Blip _deliveryBlip = null;
        private float _deliveryFillProgress = 0f;
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
        private int _fuelAmountItemCapLitres = -1;
        private NativeItem _customAmountItem;
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
        private NativeItem _hudSetExactItem;

        public FuelMod()
        {
            BuildStations();
            LoadAllData();
            BuildMenus();

            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += (s, e) => { SaveAllData(); CleanupDelivery(); };

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
            _engineOilItem = new NativeItem("Engine Oil");
            _transOilItem = new NativeItem("Transmission Oil");
            _repairItem = new NativeItem("Repair Vehicle");
            _useJerryCanItem = new NativeItem("Buy Now, Use Later");

            _serviceMenu.Add(_fuelAmountItem);
            _serviceMenu.Add(_customAmountItem);
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

            _hudSetExactItem = new NativeItem("Set HUD Position/Size...", "Type exact X,Y,Scale (e.g. 285,-260,1.0)");

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
            _settingsMenu.Add(_hudSetExactItem);
            _settingsMenu.ItemActivated += SettingsMenu_ItemActivated;
            _pool.Add(_settingsMenu);
        }

        private void ServiceMenu_ItemActivated(object sender, ItemActivatedArgs e)
        {
            if (e.Item == _fuelAmountItem) ConfirmRefuel(_fuelAmountItem.SelectedItem);
            else if (e.Item == _customAmountItem) ConfirmCustomRefuel();
            else if (e.Item == _engineOilItem) ConfirmEngineOil();
            else if (e.Item == _transOilItem) ConfirmTransOil();
            else if (e.Item == _repairItem) ConfirmRepair();
            else if (e.Item == _useJerryCanItem) BuyNowUseLater();
        }

        private void SettingsMenu_ItemActivated(object sender, ItemActivatedArgs e)
        {
            if (e.Item == _hudSetExactItem) SetHudExact();
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
                UpdateSiphonAutoTrigger();
                UpdateSiphonProgress();
                UpdateDelivery();
                UpdateAmbientVehicles();
                UpdateAutoJerryCanRefill();
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

            string plate = Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, veh);
            if (string.IsNullOrEmpty(plate)) return;
            _savedByPlate[plate] = new[] { _fuel, _engineOil, _transOil };
        }

        // Every pump gets the SAME blip name + sprite ("Saif Fuel Station").
        // GTA's own pause-map blip list groups blips that share a name+sprite
        // into one collapsible row with an arrow you cycle through - this is
        // what actually links all the pumps under one shared icon on the
        // right-side list, no custom clustering code needed.
        private const string SHARED_BLIP_NAME = "Saif Fuel Station";
        private void CreateStationBlips(Station st)
        {
            Vector3 anchor = st.Machines.Values.First();
            Blip b = World.CreateBlip(anchor);
            b.Sprite = BlipSprite.JerryCan;
            b.Color = BlipColor.Green;
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

                float litresPerSecond = (6.5f / 100f) * modifier;
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
            _engineOilItem.Description = $"${(ENGINE_OIL_MAX - _engineOil) * ENGINE_OIL_PRICE_PER_UNIT:F2}";
            _transOilItem.Description = $"${(TRANS_OIL_MAX - _transOil) * TRANS_OIL_PRICE_PER_UNIT:F2}";
            _repairItem.Description = $"${GetRepairCost(veh):F2}";
            _useJerryCanItem.Description = $"Jerry can: {_jerryCanFuel:F1}/{JERRY_CAN_CAPACITY:F0}L stored";
            _serviceMenu.Name = $"{station.Name} ({type})";

            if (!_serviceMenu.Visible) _serviceMenu.Visible = true;
        }

        private void ConfirmRefuel(float litres)
        {
            if (litres <= 0.5f) { ShowToast("~y~Tank is already full."); return; }
            float cost = litres * GetFuelPrice(_nearFuelType, _nearStation);
            StartJob(ActiveJob.Refuel, cost, "Refueling", litres);
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

        private void ConfirmEngineOil()
        {
            float cost = (ENGINE_OIL_MAX - _engineOil) * ENGINE_OIL_PRICE_PER_UNIT;
            if (cost <= 0.5f) { ShowToast("~y~Engine oil is already full."); return; }
            StartJob(ActiveJob.ChangeEngineOil, cost, "Changing engine oil", 0);
        }

        private void ConfirmTransOil()
        {
            float cost = (TRANS_OIL_MAX - _transOil) * TRANS_OIL_PRICE_PER_UNIT;
            if (cost <= 0.5f) { ShowToast("~y~Transmission oil is already full."); return; }
            StartJob(ActiveJob.ChangeTransOil, cost, "Changing transmission oil", 0);
        }

        private void ConfirmRepair()
        {
            Vehicle veh = Game.Player.Character.CurrentVehicle;
            float cost = GetRepairCost(veh);
            if (cost <= 0.5f) { ShowToast("~y~Your vehicle is in great condition."); return; }
            StartJob(ActiveJob.Repair, cost, "Repairing vehicle", 0);
        }

        // "Buy Now, Use Later" - pay for fuel at today's pump price, but it
        // goes straight into the jerry can instead of the tank, so it's
        // banked for whenever you want it. Actual pouring into a vehicle
        // still happens automatically via the jerry-can proximity system.
        private void BuyNowUseLater()
        {
            float space = Math.Max(0f, JERRY_CAN_CAPACITY - _jerryCanFuel);
            if (space <= 0.2f) { ShowToast("~y~Jerry can is already full."); return; }

            string input = Game.GetUserInput(WindowTitle.EnterMessage60, $"{(int)space}", 5);
            if (string.IsNullOrWhiteSpace(input)) return;
            if (!float.TryParse(input, out float litres) || litres <= 0f)
            {
                ShowToast("~r~Enter a valid number of litres.");
                return;
            }

            litres = Math.Min(litres, space);
            float cost = litres * GetFuelPrice(_nearFuelType, _nearStation);
            if (_money < cost) { ShowToast("~r~Your money is not enough."); return; }

            _money -= (int)Math.Round(cost);
            _jerryCanFuel += litres;
            ShowToast($"~g~Bought {litres:F1}L~w~ for later (${cost:F2}) - stored in jerry can.");
            SaveAllData();
        }

        private void StartJob(ActiveJob job, float cost, string label, float litres)
        {
            if (_money < cost) { ShowToast("~r~Your money is not enough."); return; }
            _money -= (int)Math.Round(cost);
            _activeJob = job;
            _jobTargetFuel = job == ActiveJob.Refuel ? Math.Min(_maxFuel, _fuel + litres) : 0f;
            Vehicle veh = Game.Player.Character.CurrentVehicle;
            _jobStartValue = job == ActiveJob.Refuel ? _fuel
                : job == ActiveJob.ChangeEngineOil ? _engineOil
                : job == ActiveJob.ChangeTransOil ? _transOil
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
                case ActiveJob.ChangeEngineOil:
                    _engineOil = Math.Min(ENGINE_OIL_MAX, _engineOil + _oilChangeSpeed * Game.LastFrameTime);
                    jobPct = Math.Min(1f, (_engineOil - _jobStartValue) / Math.Max(0.01f, ENGINE_OIL_MAX - _jobStartValue));
                    jobLabel = "Changing engine oil"; jobColor = Color.FromArgb(255, 200, 140, 40);
                    if (_engineOil >= ENGINE_OIL_MAX - 0.5f) { _activeJob = ActiveJob.None; ShowToast("~g~Engine oil changed!"); SaveAllData(); }
                    break;
                case ActiveJob.ChangeTransOil:
                    _transOil = Math.Min(TRANS_OIL_MAX, _transOil + _oilChangeSpeed * Game.LastFrameTime);
                    jobPct = Math.Min(1f, (_transOil - _jobStartValue) / Math.Max(0.01f, TRANS_OIL_MAX - _jobStartValue));
                    jobLabel = "Changing transmission oil"; jobColor = Color.FromArgb(255, 200, 140, 40);
                    if (_transOil >= TRANS_OIL_MAX - 0.5f) { _activeJob = ActiveJob.None; ShowToast("~g~Transmission oil changed!"); SaveAllData(); }
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
        // SIPHONING - progress bar, fills jerry can (triggered from Settings menu)
        // =================================================================
        // Checks whether any nearby ped is actually facing the player with a clear
        // line of sight - real witness detection instead of a flat dice roll.
        private bool WasSeenByWitness(Ped playerPed)
        {
            foreach (Ped p in World.GetNearbyPeds(playerPed.Position, 15f))
            {
                if (p == null || !p.Exists() || p == playerPed || p.IsDead) continue;

                Vector3 toPlayer = (playerPed.Position - p.Position);
                if (toPlayer.Length() < 0.1f) continue;
                toPlayer = toPlayer.Normalized;

                float facing = Vector3.Dot(p.ForwardVector, toPlayer); // 1 = looking straight at player
                if (facing < 0.6f) continue; // outside roughly a 100 degree cone

                if (Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, p, playerPed, 17))
                    return true;
            }
            return false;
        }

        // Auto-triggered every tick instead of a menu item: only starts when
        // the player already has the jerry can equipped AND is standing right
        // up against a vehicle's fuel cap (tight range, not just "nearby").
        private void UpdateSiphonAutoTrigger()
        {
            if (_theftInProgress) return;
            Ped playerPed = Game.Player.Character;
            if (playerPed.IsInVehicle()) return;
            if (playerPed.Weapons.Current == null || playerPed.Weapons.Current.Hash != WeaponHash.PetrolCan) return;
            if (_jerryCanFuel >= JERRY_CAN_CAPACITY - 0.5f) return;

            Vehicle nearest = null;
            float bestDist = 1.6f; // right at the fuel cap, not just "nearby"
            foreach (Vehicle v in World.GetNearbyVehicles(playerPed.Position, 1.6f))
            {
                if (v == null || !v.Exists() || v == playerPed.CurrentVehicle) continue;
                if (v.Driver != null && v.Driver.Exists() && !v.Driver.IsDead) continue;
                float d = v.Position.DistanceTo(playerPed.Position);
                if (d < bestDist) { bestDist = d; nearest = v; }
            }
            if (nearest == null) return;

            TryStartSiphon(nearest);
        }

        private void TryStartSiphon(Vehicle nearest)
        {
            Ped playerPed = Game.Player.Character;

            _theftTarget = nearest;
            _theftInProgress = true;
            _theftProgress = 0f;

            Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, playerPed, nearest, 1500);
            playerPed.Task.PlayAnimation("amb@prop_human_parking_meter@male@base", "base", 8.0f, -1, AnimationFlags.Loop);
            ShowToast("~y~Siphoning fuel...~w~ hold still.");
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
                ShowToast("~r~Siphoning interrupted.");
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
                ShowToast($"~g~Siphoned {stolen:F1}L~w~ into your jerry can.");

                if (WasSeenByWitness(playerPed))
                {
                    Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, Game.Player, 1, false);
                    Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, Game.Player, false);
                    ShowToast("~r~Someone saw you!");
                }
                SaveAllData();
            }
        }

        // =================================================================
        // FUEL DELIVERY BY PLANE
        // =================================================================
        private Vector3 _deliveryStartPos;
        private Vector3 _deliveryTargetPos;
        private float _deliveryFlightT;
        private const float DELIVERY_FLIGHT_SECONDS = 28f;

        private bool _deliveryToTankDirect; // true = boat/air (fills tank), false = land (fills jerry can)
        private const float HOVER_DURATION_SECONDS = 12f;

        private void TryCallDelivery()
        {
            if (_deliveryPhase != DeliveryPhase.None) { ShowToast("~y~A delivery is already on its way."); return; }
            if (_money < DELIVERY_COST) { ShowToast($"~r~Not enough money (${DELIVERY_COST})."); return; }

            Ped playerPed = Game.Player.Character;

            // Always fly to the PLAYER now (not a station) so the hose visual
            // and hover actually happen where the player can see them.
            bool boatOrAir = playerPed.IsInVehicle() &&
                (playerPed.CurrentVehicle.ClassType == VehicleClass.Boats ||
                 playerPed.CurrentVehicle.ClassType == VehicleClass.Planes ||
                 playerPed.CurrentVehicle.ClassType == VehicleClass.Helicopters);
            _deliveryToTankDirect = boatOrAir;

            _deliveryTargetPos = playerPed.Position + new Vector3(0, 0, 22f); // hover height above player
            _deliveryStartPos = _deliveryTargetPos + (playerPed.ForwardVector * -500f) + new Vector3(0, 0, 60f);

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

            _money -= DELIVERY_COST;
            _deliveryPhase = DeliveryPhase.Inbound;
            _deliveryFlightT = 0f;
            _deliveryFillProgress = 0f;
            ShowToast($"~g~Fuel delivery dispatched~w~ (${DELIVERY_COST}). Watch the sky above you.");
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
                        _deliveryTargetPos = playerPed.Position + new Vector3(0, 0, 22f);

                        _deliveryFlightT += Game.LastFrameTime / DELIVERY_FLIGHT_SECONDS;
                        float t = Math.Min(1f, _deliveryFlightT);
                        Vector3 pos = Vector3.Lerp(_deliveryStartPos, _deliveryTargetPos, t);
                        MovePlaneTo(pos);

                        float dist = _deliveryPlane.Position.DistanceTo(playerPed.Position);
                        new TextElement($"Fuel delivery inbound: {dist:F0}m", new PointF(20, 60), 0.26f, Color.FromArgb(255, 255, 220, 120), Font.ChaletLondon).Draw();

                        if (t >= 1f)
                        {
                            _deliveryPhase = DeliveryPhase.Hovering;
                            _deliveryFillProgress = 0f;
                        }
                        break;
                    }

                case DeliveryPhase.Hovering:
                    {
                        // stay locked above the player, facing down toward them
                        Vector3 hoverPos = playerPed.Position + new Vector3(0, 0, 22f);
                        MovePlaneTo(hoverPos, faceDown: true);

                        // hose/pipe visual: dotted line from under the heli down to the player
                        Vector3 hoseStart = hoverPos + new Vector3(0, 0, -3f);
                        Vector3 hoseEnd = playerPed.Position + new Vector3(0, 0, 1f);
                        DrawFuelHose(hoseStart, hoseEnd, Color.FromArgb(230, 255, 200, 60));

                        _deliveryFillProgress += Game.LastFrameTime / HOVER_DURATION_SECONDS;
                        float pct = Math.Min(1f, _deliveryFillProgress);

                        if (_deliveryToTankDirect)
                            _fuel = Math.Min(_maxFuel, _maxFuel * pct);
                        else
                            _jerryCanFuel = Math.Min(JERRY_CAN_CAPACITY, JERRY_CAN_CAPACITY * pct);

                        // on-screen fill progress bar
                        float barX = 20f, barY = 90f;
                        new ContainerElement(new PointF(barX, barY), new SizeF(300, 30), Color.FromArgb(200, 20, 20, 20)).Draw();
                        new ContainerElement(new PointF(barX + 5, barY + 20), new SizeF(290, 6), Color.FromArgb(150, 60, 60, 60)).Draw();
                        new ContainerElement(new PointF(barX + 5, barY + 20), new SizeF(290 * pct, 6), Color.FromArgb(255, 255, 200, 60)).Draw();
                        string target = _deliveryToTankDirect ? "tank" : "jerry can";
                        new TextElement($"Fuel delivery hovering - filling {target}... {(int)(pct * 100)}%", new PointF(barX + 5, barY + 2), 0.24f, Color.White, Font.ChaletLondon).Draw();

                        if (_deliveryFillProgress >= 1f)
                        {
                            ShowToast(_deliveryToTankDirect ? "~g~Tank filled by air delivery!" : "~g~Jerry can filled by air delivery!");
                            SaveAllData();
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
        private void DrawFuelHose(Vector3 fromWorld, Vector3 toWorld, Color color)
        {
            if (!WorldToScreen(fromWorld, out float x1, out float y1)) return;
            if (!WorldToScreen(toWorld, out float x2, out float y2)) return;

            const int SEGMENTS = 16;
            for (int i = 0; i <= SEGMENTS; i++)
            {
                float t = i / (float)SEGMENTS;
                float px = x1 + (x2 - x1) * t;
                float py = y1 + (y2 - y1) * t;
                new ContainerElement(new PointF(px - 3, py - 3), new SizeF(6, 6), color).Draw();
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
            if (_deliveryBlip != null && _deliveryBlip.Exists()) _deliveryBlip.Delete();
            if (_deliveryPilot != null && _deliveryPilot.Exists()) _deliveryPilot.Delete();
            if (_deliveryPlane != null && _deliveryPlane.Exists()) _deliveryPlane.Delete();
            _deliveryBlip = null; _deliveryPilot = null; _deliveryPlane = null;
            _deliveryPhase = DeliveryPhase.None;
        }

        // =================================================================
        // HUD - single combined bar-shape box with 3 vertical columns side
        // by side: Fuel (green/red), Oil (amber/brown), Speed (cyan, scaled
        // 0-240 km/h). One shared backing panel instead of 3 separate boxes.
        // =================================================================
        private void UpdateHud()
        {
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) return; // no HUD on foot
            Vehicle veh = playerPed.CurrentVehicle;
            if (veh == null || !veh.Exists()) return;

            const int SEGMENTS = 10;

            float barHeight = 220f * _hudScale;
            float barTop = Screen.Height + _hudOffsetY - barHeight; // _hudOffsetY is negative
            float segGap = 3f;
            float colW = 26f * _hudScale;
            float segH = (barHeight - segGap * (SEGMENTS - 1)) / SEGMENTS;

            float panelX = _hudOffsetX;
            new ContainerElement(new PointF(panelX - 6, barTop - 6), new SizeF(colW + 12, barHeight + 12), Color.FromArgb(160, 0, 0, 0)).Draw();

            // ---- FUEL (only) ----
            float fuelPct = _maxFuel > 0 ? _fuel / _maxFuel : 0f;
            _displayedFuelPct += (fuelPct - _displayedFuelPct) * Math.Min(1f, Game.LastFrameTime * 2.5f);
            bool fuelLow = fuelPct < 0.2f;
            DrawHudColumn(panelX, barTop, colW, segH, segGap, SEGMENTS, _displayedFuelPct,
                Color.LimeGreen, Color.Red, fuelLow);
        }

        // Draws one vertical segmented column (bottom-up fill).
        private void DrawHudColumn(float x, float barTop, float colW, float segH, float segGap, int segments, float pct, Color normalColor, Color lowColor, bool isLow)
        {
            int lit = (int)Math.Round(Math.Max(0f, Math.Min(1f, pct)) * segments);
            for (int i = 0; i < segments; i++)
            {
                bool on = i >= (segments - lit);
                Color c = !on ? Color.FromArgb(150, 45, 45, 45) : (isLow ? lowColor : normalColor);
                float segY = barTop + i * (segH + segGap);
                new ContainerElement(new PointF(x, segY), new SizeF(colW, segH), c).Draw();
            }
        }

        // =================================================================
        // AIRCRAFT THREAT RADAR - enemy fighter jet detector + guided
        // missile lock warning, active only while flying a plane/helicopter.
        // Heuristic-based: GTA doesn't expose a direct "incoming missile"
        // native, so a hostile aircraft is flagged as a lock threat once it
        // is close AND pointed roughly at the player (within the missile's
        // realistic engagement cone) - the same signal real lock-warning
        // systems key off in-game.
        // =================================================================
        private const float THREAT_SCAN_RADIUS = 900f;
        private const float MISSILE_LOCK_RANGE = 350f;
        private const float MISSILE_LOCK_CONE_DOT = 0.90f; // ~25 degrees
        private DateTime _lastThreatBeep = DateTime.MinValue;

        private void UpdateAircraftThreatRadar()
        {
            Ped playerPed = Game.Player.Character;
            if (!playerPed.IsInVehicle()) return;
            Vehicle myVeh = playerPed.CurrentVehicle;
            if (myVeh.ClassType != VehicleClass.Planes && myVeh.ClassType != VehicleClass.Helicopters) return;

            var threats = new List<(Vehicle veh, float dist, bool locking)>();
            bool anyLock = false;

            foreach (Vehicle v in World.GetNearbyVehicles(myVeh.Position, THREAT_SCAN_RADIUS))
            {
                if (v == null || !v.Exists() || v == myVeh) continue;
                if (v.ClassType != VehicleClass.Planes && v.ClassType != VehicleClass.Helicopters) continue;
                Ped driver = v.Driver;
                if (driver == null || !driver.Exists() || driver.IsDead) continue;

                // hostile = actively in combat and targeting us, or a
                // wanted-level military response unit (Hunter/Lazer etc.)
                bool hostile = Function.Call<bool>(Hash.IS_PED_IN_COMBAT, driver, playerPed);
                if (!hostile) continue;

                float dist = v.Position.DistanceTo(myVeh.Position);
                Vector3 toMe = (myVeh.Position - v.Position).Normalized;
                float aim = Vector3.Dot(v.ForwardVector, toMe);
                bool locking = dist <= MISSILE_LOCK_RANGE && aim >= MISSILE_LOCK_CONE_DOT;

                threats.Add((v, dist, locking));
                if (locking) anyLock = true;
            }

            DrawThreatRadar(myVeh, threats);

            if (anyLock)
            {
                new TextElement("~r~MISSILE LOCK WARNING", new PointF(Screen.Width / 2f - 140, 40), 0.5f, Color.Red, Font.ChaletComprimeCologne, GTA.UI.Alignment.Left, true, true).Draw();
                if ((DateTime.Now - _lastThreatBeep).TotalMilliseconds > 700)
                {
                    _lastThreatBeep = DateTime.Now;
                    Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "5_SEC_WARNING", "MP_LEADERBOARD_SOUNDSET", false);
                }
            }
        }

        // Small top-right radar circle: player at center, hostile aircraft
        // plotted as dots rotated relative to the player's heading, red for
        // an active missile-lock threat, yellow otherwise.
        private void DrawThreatRadar(Vehicle myVeh, List<(Vehicle veh, float dist, bool locking)> threats)
        {
            float radius = 70f;
            float cx = Screen.Width - 110f;
            float cy = 140f;

            new ContainerElement(new PointF(cx - radius - 6, cy - radius - 6), new SizeF(radius * 2 + 12, radius * 2 + 12), Color.FromArgb(160, 0, 0, 0)).Draw();
            new ContainerElement(new PointF(cx - 2, cy - 2), new SizeF(4, 4), Color.White).Draw(); // player dot, always centered

            if (threats.Count == 0)
            {
                new TextElement("No threats", new PointF(cx - 40, cy + radius + 6), 0.22f, Color.FromArgb(200, 180, 180, 180), Font.ChaletLondon).Draw();
                return;
            }

            float myHeadingRad = myVeh.Heading * (float)(Math.PI / 180.0);
            foreach (var t in threats)
            {
                Vector3 rel = t.veh.Position - myVeh.Position;
                // rotate relative position into player-heading-relative space
                float rx = rel.X * (float)Math.Cos(-myHeadingRad) - rel.Y * (float)Math.Sin(-myHeadingRad);
                float ry = rel.X * (float)Math.Sin(-myHeadingRad) + rel.Y * (float)Math.Cos(-myHeadingRad);

                float scale = Math.Min(1f, t.dist / THREAT_SCAN_RADIUS);
                float px = cx + rx * (radius / THREAT_SCAN_RADIUS) / Math.Max(0.15f, scale) * scale; // clamp near center for close threats
                float py = cy - ry * (radius / THREAT_SCAN_RADIUS) / Math.Max(0.15f, scale) * scale;
                px = Math.Max(cx - radius, Math.Min(cx + radius, px));
                py = Math.Max(cy - radius, Math.Min(cy + radius, py));

                Color dotColor = t.locking ? Color.Red : Color.Yellow;
                new ContainerElement(new PointF(px - 4, py - 4), new SizeF(8, 8), dotColor).Draw();
            }

            new TextElement($"{threats.Count} hostile aircraft", new PointF(cx - 55, cy + radius + 6), 0.22f, Color.FromArgb(220, 255, 120, 120), Font.ChaletLondon).Draw();
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
                                if (parts.Length == 5 &&
                                    float.TryParse(parts[1], out float f) &&
                                    float.TryParse(parts[2], out float eo) &&
                                    float.TryParse(parts[3], out float to) &&
                                    float.TryParse(parts[4], out float jc))
                                {
                                    _savedByPlate[parts[0]] = new[] { f, eo, to };
                                    _jerryCanFuel = jc; // global, not per-vehicle
                                }
                                break;
                            }
                        case "[CONFIG]":
                            {
                                var parts = line.Split('|');
                                if (parts.Length >= 8)
                                {
                                    _fuelConsumeRate = float.Parse(parts[0]);
                                    _oilConsumeRate = float.Parse(parts[1]);
                                    _refuelSpeed = float.Parse(parts[2]);
                                    _oilChangeSpeed = float.Parse(parts[3]);
                                    _oilAffectsEngine = parts[4] == "true";
                                    _priceRegular = float.Parse(parts[5]);
                                    _priceDiesel = float.Parse(parts[6]);
                                    _pricePremium = float.Parse(parts[7]);
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

        // Writes the whole combined file every time - called from all the
        // same places SaveAllData()/SaveConfig()/SaveHudConfig() used to be
        // called from, so nothing else needs to change at the call sites.
        private void SaveAllData()
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

                var lines = new List<string> { "[SAVE]" };
                lines.AddRange(_savedByPlate.Select(kv => $"{kv.Key}|{kv.Value[0]:F1}|{kv.Value[1]:F1}|{kv.Value[2]:F1}|{_jerryCanFuel:F1}"));

                lines.Add("[CONFIG]");
                lines.Add(string.Join("|",
                    _fuelConsumeRate.ToString("F2"), _oilConsumeRate.ToString("F2"),
                    _refuelSpeed.ToString("F1"), _oilChangeSpeed.ToString("F0"),
                    _oilAffectsEngine.ToString().ToLower(),
                    _priceRegular.ToString("F2"), _priceDiesel.ToString("F2"), _pricePremium.ToString("F2")));

                lines.Add("[HUD]");
                lines.Add($"{_hudOffsetX:F0}|{_hudOffsetY:F0}|{_hudScale:F1}");

                File.WriteAllLines(DataPath, lines);
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

        // =================================================================
        // KEY HANDLING - F7 (settings), X (radio), E (jerry can top-up),
        // H (call fuel delivery). Menus themselves are driven entirely by
        // arrow keys + Enter via LemonUI.
        // =================================================================
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.KeyCode == Keys.F7)
                {
                    bool opening = !_settingsMenu.Visible;
                    _settingsMenu.Visible = opening;
                    if (!opening) { SaveAllData(); }
                    return;
                }

                if (_settingsMenu.Visible || _serviceMenu.Visible) return; // let LemonUI handle nav keys

                if (e.KeyCode == Keys.X) ToggleRadio();
                else if (e.KeyCode == Keys.E) TryQuickJerryCanRefill();
                else if (e.KeyCode == Keys.H) TryCallDelivery();
            }
            catch (Exception ex) { Log("OnKeyDown error: " + ex.Message); }
        }

        // On foot next to your own vehicle, E tops it up from the jerry can
        // directly - no need to open a menu for this simple action.
        private void TryQuickJerryCanRefill()
        {
            DoJerryCanRefill(true);
        }

        // Auto version: fires every tick while the jerry can is the equipped
        // weapon and the player is standing close to one of their own
        // tracked vehicles - no key press needed, matches "walk up = it just
        // pours" behaviour. Rate-limited so it doesn't spam notifications.
        private DateTime _lastAutoJerryRefill = DateTime.MinValue;
        private void UpdateAutoJerryCanRefill()
        {
            Ped playerPed = Game.Player.Character;
            if (playerPed.IsInVehicle()) return;
            if (playerPed.Weapons.Current == null || playerPed.Weapons.Current.Hash != WeaponHash.PetrolCan) return;
            if (_jerryCanFuel <= 0.2f) return;
            if ((DateTime.Now - _lastAutoJerryRefill).TotalMilliseconds < 800) return;

            _lastAutoJerryRefill = DateTime.Now;
            DoJerryCanRefill(false);
        }

        private void DoJerryCanRefill(bool notifyIfFull)
        {
            Ped playerPed = Game.Player.Character;
            if (playerPed.IsInVehicle()) return;
            if (_jerryCanFuel <= 0.2f) return;

            Vehicle nearest = null;
            float bestDist = 3.0f;
            foreach (Vehicle v in World.GetNearbyVehicles(playerPed.Position, 3.0f))
            {
                if (v == null || !v.Exists()) continue;
                float d = v.Position.DistanceTo(playerPed.Position);
                if (d < bestDist) { bestDist = d; nearest = v; }
            }
            if (nearest == null) return;

            string plate = Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, nearest);
            if (string.IsNullOrEmpty(plate) || !_savedByPlate.TryGetValue(plate, out float[] saved)) return; // only your own tracked vehicles

            float maxFuelForThis = GetTankSize(nearest);
            float space = Math.Max(0f, maxFuelForThis - saved[0]);
            float transfer = Math.Min(_jerryCanFuel, space);
            if (transfer <= 0.2f) { if (notifyIfFull) ShowToast("~y~That tank is already full."); return; }

            saved[0] += transfer;
            _jerryCanFuel -= transfer;
            if (nearest.Handle == _lastVehicleHandle) _fuel = saved[0]; // currently-tracked vehicle, keep in sync
            ShowToast($"~g~Poured {transfer:F1}L~w~ from your jerry can.");
            SaveAllData();
        }

        private static void Log(string msg)
        {
            try { File.AppendAllText("scripts\\SaifFuelMod.log", $"[{DateTime.Now:HH:mm:ss}] {msg}\r\n"); }
            catch { /* ignore */ }
        }
    }
}
