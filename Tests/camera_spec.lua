-- Exercises the production camera/localization code with game APIs replaced by deterministic doubles.
-- No game files or proprietary scripts are included.
local filename = arg[1] or "Resources/Scripts/UserScriptLocal.lua"
local file = assert(io.open(filename, "r"))
local source = file:read("*a")
file:close()
local start = assert(source:find("S6Patcher.GetLocalizedText = function", 1, true))
local cameraSource = source:sub(start)
local compile = loadstring or load
local tests, failures = 0, 0
local state
local function eq(a, b, label)
    assert(a == b, (label or "value") .. ": expected " .. tostring(b) .. ", got " .. tostring(a))
end
local function setup(language, market)
    state = {language = language or "en", market = market == nil and 42 or market,
        animation = false, network = false, camera = 0, pages = 0, switches = 0,
        mouseX = 900, mouseY = 450, time = 1000, selected = 0, original = {}, widgets = {},
        down = {}, up = {}, logs = {}, notes = {}, option = 1}
    S6Patcher = {DisableFeatures = false}
    g_Throneroom = nil
    Options = {GetIntValue = function(_, _, default) return state.option or default end}
    Keys = {Z = 90, W = 87, S = 83, U = 85, T = 84, R = 82, P = 80}
    local keyNames = {KeyMenuWeather = "W", KeyBuildingStartStop = "S", KeyBuildingUpgrade = "U",
        KeyBuildTrail = "T", KeyBuildStreet = "R", KeyMenuProduction = "P"}
    Input = {KeyBindDown = function(key, call) assert(key, "invalid key"); state.down[key] = call end,
        KeyBindUp = function(key, call) assert(key, "invalid key"); state.up[key] = call end}
    XGUIEng = {
        GetStringTableText = function(path) return keyNames[path:match("KeyBindings/(.+)")] or "Baths" end,
        ShowWidget = function(path, shown) state.widgets[path] = shown end,
        ShowAllSubWidgets = function(path, shown) state.widgets[path] = shown end,
        IsWidgetShown = function(path) local v = state.widgets[path]; if v == nil then return 1 end; return v end,
        PushPage = function() state.pages = state.pages + 1 end,
        PopPage = function() state.pages = state.pages - 1; assert(state.pages >= 0, "page underflow") end}
    Framework = {IsNetworkGame = function() return state.network end, GetTimeMs = function() return state.time end,
        WriteToLog = function(message) table.insert(state.logs, message) end}
    Network = {GetDesiredLanguage = function() return state.language end}
    CameraAnimation = {IsRunning = function() return state.animation end}
    Logic = {GetMarketplace = function() return state.market end,
        GetBuildingApproachPosition = function(id) assert(id ~= 0, "market missing"); return 1200, 2300 end,
        GetPlayerSectorAtPosition = function() return 1 end}
    local function noop() end
    Display = {GetTerrainHeight = function() return 25 end, SetRenderSky = noop, SetRenderFogOfWar = noop,
        SetRenderBorderPins = noop, SetUserOptionOcclusionEffect = noop, UseStandardSettings = noop,
        UseCloseUpSettings = noop, SetFarClipPlaneMinAndMax = noop}
    Camera = {SwitchCameraBehaviour = function(id) state.camera = id; state.switches = state.switches + 1 end,
        ThroneRoom_SetPosition = noop, ThroneRoom_SetLookAt = noop, ThroneRoom_SetFOV = noop,
        RTS_GetLookAtPosition = function() return 3000, 4000 end, RTS_SetLookAtPosition = noop}
    GUI = {GetPlayerID = function() return 1 end, GetMousePosition = function() return state.mouseX, state.mouseY end,
        GetSelectedEntity = function() return state.selected end, ClearSelection = function()
            local hadSelection = state.selected ~= 0
            state.selected = 0
            if hadSelection then GameCallback_GUI_SelectionChanged(1) end
        end, ClearNotes = noop, AddNote = function(text) assert(type(text) == "string"); table.insert(state.notes, text) end,
        ForbidContextSensitiveCommandsInSelectionState = noop, PermitContextSensitiveCommandsInSelectionState = noop,
        SetFeedbackSoundOutputState = noop, EnableBattleSignals = noop}
    Game = {GameTimeSetFactor = function() state.speedChanges = (state.speedChanges or 0) + 1 end}
    for _, name in ipairs({"ThroneRoomCameraControl", "GameCallback_GUI_SelectionChanged", "KeyBindings_SaveGame",
        "GameCallback_Escape", "OnBackButtonPressed", "OnSkipButtonPressed", "ThroneRoomLeftClick",
        "KeyBindings_MenuWeather", "KeyBindings_BuildingStartStop"}) do
        _G[name] = function() state.original[name] = (state.original[name] or 0) + 1; return name end
    end
    assert(compile(cameraSource, "@" .. filename))()
end
local function test(name, body)
    tests = tests + 1
    local ok, err = pcall(body)
    if ok then print("PASS " .. name) else failures = failures + 1; print("FAIL " .. name .. ": " .. tostring(err)) end
end
test("French activation and exit", function()
    setup("fr"); S6Patcher.FPSMode.Toggle(); eq(S6Patcher.FPSMode.Enabled, true)
    GameCallback_Escape(); eq(S6Patcher.FPSMode.Enabled, false); eq(state.camera, 0); eq(state.pages, 0)
end)
test("missing market leaves strategic camera untouched", function()
    setup("en", 0); S6Patcher.FPSMode.Toggle(); eq(S6Patcher.FPSMode.Enabled, false)
    eq(state.switches, 0); eq(state.pages, 0)
end)
test("no inhabitants are required when the market exists", function()
    setup(); S6Patcher.FPSMode.Toggle(); eq(S6Patcher.FPSMode.Enabled, true)
    S6Patcher.FPSMode.Toggle(); eq(state.camera, 0)
end)
test("camera preserves simulation speed", function()
    setup(); S6Patcher.FPSMode.Toggle(); S6Patcher.FPSMode.Toggle(); eq(state.speedChanges, nil)
end)
test("twenty toggles balance UI pages and restore hidden widgets", function()
    setup(); local path = "/InGame/Root/Normal/AlignTopLeft/QuestTimers"; state.widgets[path] = 0
    for i = 1, 20 do S6Patcher.FPSMode.Toggle(); S6Patcher.FPSMode.Toggle() end
    eq(state.pages, 0); eq(state.widgets[path], 0); eq(S6Patcher.FPSMode.Enabled, false)
end)
test("exit remains available after feature restrictions change", function()
    setup(); S6Patcher.FPSMode.Toggle(); S6Patcher.DisableFeatures = true
    S6Patcher.FPSMode.Toggle(); eq(S6Patcher.FPSMode.Enabled, false); eq(state.camera, 0)
end)
test("cinematic interruption releases FPS without replacing its camera", function()
    setup(); S6Patcher.FPSMode.Toggle(); g_Throneroom = {}; state.camera = 2
    ThroneRoomCameraControl(); eq(S6Patcher.FPSMode.Enabled, false); eq(state.camera, 2)
    eq(state.original.ThroneRoomCameraControl, 1)
end)
test("animation interruption releases FPS", function()
    setup(); S6Patcher.FPSMode.Toggle(); state.animation = true; state.camera = 1
    ThroneRoomCameraControl(); eq(S6Patcher.FPSMode.Enabled, false); eq(state.camera, 1)
end)
test("normal throne-room callbacks delegate while FPS is inactive", function()
    setup(); eq(OnBackButtonPressed(), "OnBackButtonPressed")
    eq(OnSkipButtonPressed(), "OnSkipButtonPressed"); eq(ThroneRoomLeftClick(), "ThroneRoomLeftClick")
end)
test("backward key restores building start-stop", function()
    setup(); S6Patcher.FPSMode.Toggle(); S6Patcher.FPSMode.Toggle()
    assert(compile(state.down[Keys.S]))(); eq(state.original.KeyBindings_BuildingStartStop, 1)
end)
test("entry failure restores camera and allows a fresh attempt", function()
    setup(); local original = Camera.ThroneRoom_SetFOV
    Camera.ThroneRoom_SetFOV = function() error("injected FOV failure") end
    S6Patcher.FPSMode.Toggle(); eq(S6Patcher.FPSMode.Enabled, false); eq(state.camera, 0); eq(state.pages, 0)
    Camera.ThroneRoom_SetFOV = original; S6Patcher.FPSMode.Toggle(); eq(S6Patcher.FPSMode.Enabled, true)
end)
test("frame failure restores controls", function()
    setup(); S6Patcher.FPSMode.Toggle()
    Camera.ThroneRoom_SetLookAt = function() error("injected frame failure") end
    ThroneRoomCameraControl(); eq(S6Patcher.FPSMode.Enabled, false); eq(state.camera, 0)
end)
test("movement and mouse origin reset on each activation", function()
    setup(); S6Patcher.FPSMode.Toggle(); S6Patcher.FPSMode.ToggleMovement(true, 0)
    S6Patcher.FPSMode.Toggle(); S6Patcher.FPSMode.Toggle()
    eq(S6Patcher.FPSMode.IsMoving, false); eq(S6Patcher.FPSMode.LastMouseX, state.mouseX)
    eq(S6Patcher.FPSMode.LastMouseY, state.mouseY)
end)
test("selection callback clears a character without recursion", function()
    setup(); state.selected = 9; S6Patcher.FPSMode.Toggle()
    state.selected = 10; GameCallback_GUI_SelectionChanged(1); eq(state.selected, 0)
end)
test("escape delegates normally after FPS exit", function()
    setup(); S6Patcher.FPSMode.Toggle(); GameCallback_Escape(); eq(state.original.GameCallback_Escape, nil)
    GameCallback_Escape(); eq(state.original.GameCallback_Escape, 1)
end)
test("script reload cleans up active FPS", function()
    setup(); S6Patcher.FPSMode.Toggle(); assert(compile(cameraSource))()
    eq(S6Patcher.FPSMode.Enabled, false); eq(state.pages, 0); eq(state.camera, 0)
end)
test("camera opt-out does not disable other features", function()
    setup(); state.option = 0; S6Patcher.FPSMode.Toggle()
    eq(S6Patcher.FPSMode.Enabled, false); eq(S6Patcher.DisableFeatures, false); eq(state.switches, 0)
end)
test("missing and conflicting bindings reject entry before mutation", function()
    setup(); S6Patcher.FPSMode.KeyMapping.Backward = "Missing"
    S6Patcher.FPSMode.Toggle(); eq(state.switches, 0); eq(S6Patcher.FPSMode.Enabled, false)
    setup(); S6Patcher.FPSMode.KeyMapping.Backward = "W"
    S6Patcher.FPSMode.Toggle(); eq(state.switches, 0)
end)
test("recovery attempts all steps and can be retried after a failure", function()
    setup(); S6Patcher.FPSMode.Toggle(); local original = Display.UseStandardSettings
    Display.UseStandardSettings = function() error("injected recovery failure") end
    GameCallback_Escape(); eq(S6Patcher.FPSMode.Enabled, false); eq(state.camera, 0)
    eq(S6Patcher.FPSMode.CleanupPending, true); eq(state.pages, 0)
    Display.UseStandardSettings = original; GameCallback_Escape()
    eq(S6Patcher.FPSMode.CleanupPending, false); eq(state.pages, 0)
end)
test("invalid coordinates are rejected before camera switch", function()
    setup(); Logic.GetBuildingApproachPosition = function() return nil, 2 end
    S6Patcher.FPSMode.Toggle(); eq(state.switches, 0); eq(S6Patcher.FPSMode.Enabled, false)
    setup(); Logic.GetBuildingApproachPosition = function() return 0/0, 2 end
    S6Patcher.FPSMode.Toggle(); eq(state.switches, 0)
end)
test("releasing one movement key preserves the other held key", function()
    setup(); S6Patcher.FPSMode.Toggle(); local FPS = S6Patcher.FPSMode
    FPS.ToggleMovement(true, FPS.Directions.Forward); FPS.ToggleMovement(true, FPS.Directions.Backward)
    FPS.ToggleMovement(false, FPS.Directions.Backward)
    eq(FPS.IsMoving, true); eq(FPS.CurrentDirection, FPS.Directions.Forward)
    FPS.ToggleMovement(false, FPS.Directions.Forward); eq(FPS.IsMoving, false)
end)
print(string.format("Camera: %d tests, %d passed, %d failed", tests, tests - failures, failures))
os.exit(failures == 0 and 0 or 1)
