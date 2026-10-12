-- Exercises the production global script with deterministic engine doubles.
-- No game files or proprietary scripts are included. Compatible with Lua 5.1.
local filename = arg[1] or "Resources/Scripts/UserScriptGlobal.lua"
local file = assert(io.open(filename, "r"))
local source = file:read("*a")
file:close()
local compile = loadstring or load
local initialDebug = debug
local tests, failures = 0, 0
local state
local function eq(actual, expected, label)
    assert(actual == expected, (label or "value") .. ": expected " .. tostring(expected) .. ", got " .. tostring(actual))
end
local function pack(...) return {n = select("#", ...), ...} end
local function loadScript() assert(compile(source, "@" .. filename))() end
local function setup(activate, disable, proxy)
    state = {calls = {}, callbacks = {activate = 0, disable = 0}, logs = {}, restoreAttempts = 0, logAttempts = 0}
    debug = initialDebug
    S6Patcher = {}
    state.original = function(...)
        state.calls[#state.calls + 1] = {kind = "original", arguments = pack(...)}
        return "original entity"
    end
    state.unblocked = function(...)
        state.calls[#state.calls + 1] = {kind = "unblocked", arguments = pack(...)}
        return "unblocked entity"
    end
    local functions = {GetStoreHouse = function() return 0 end,
        CreateEntity = state.original, CreateEntityOnUnblockedLand = state.unblocked}
    if proxy then
        -- Expose a writable engine proxy whose setters can inject independent faults.
        Logic = setmetatable({}, {
            __index = functions,
            __newindex = function(_, key, value)
                if key == "CreateEntity" then
                    if value == state.original then
                        state.restoreAttempts = state.restoreAttempts + 1
                        if state.restoreFailure then error(state.restoreFailure, 0) end
                        if state.restoreIgnored then return end
                    end
                    if value == state.unblocked and state.installIgnored then return end
                    if value == state.unblocked and state.installFailure then
                        functions[key] = value
                        error(state.installFailure, 0)
                    end
                end
                functions[key] = value
            end})
    else
        Logic = functions
    end
    state.engine = Logic
    Framework = {GetCurrentMapTypeAndCampaignName = function() return 0, "synthetic" end,
        GetCurrentMapName = function() return "synthetic" end,
        WriteToLog = function(message)
            state.logAttempts = state.logAttempts + 1
            if state.logFailure then error("diagnostic failure", 0) end
            state.logs[#state.logs + 1] = message
        end}
    GameCallback_KnightAbilityUsed = function() end
    ActivateFireplaceforBanditPack = function(id)
        state.callbacks.activate = state.callbacks.activate + 1
        if activate then return activate(id) end
        return Logic.CreateEntity(id, 100, 200, 90, 2)
    end
    DisableFireplaceforBanditPack = function(id)
        state.callbacks.disable = state.callbacks.disable + 1
        if disable then return disable(id) end
        return Logic.CreateEntity(id, 300, 400, 180, 3)
    end
    loadScript()
end
local function test(name, body)
    tests = tests + 1
    local ok, message = pcall(body)
    if ok then print("PASS " .. name) else failures = failures + 1; print("FAIL " .. name .. ": " .. tostring(message)) end
end
local function restored()
    eq(state.engine.CreateEntity, state.original, "restored CreateEntity")
end
local function failed(callback, expected)
    local ok, message = pcall(callback, 7)
    eq(ok, false, "callback must fail")
    eq(message, expected, "original error")
end

test("activation uses the unblocked creator and restores the preceding function", function()
    setup()
    eq(ActivateFireplaceforBanditPack(7), "unblocked entity")
    restored(); eq(state.callbacks.activate, 1); eq(state.calls[1].kind, "unblocked")
    local arguments = state.calls[1].arguments
    eq(arguments.n, 5); eq(arguments[1], 7); eq(arguments[2], 100)
    eq(arguments[3], 200); eq(arguments[4], 90); eq(arguments[5], 2)
    eq(Logic.CreateEntity(), "original entity")
end)
test("deactivation uses the unblocked creator and restores the preceding function", function()
    setup()
    eq(DisableFireplaceforBanditPack(8), "unblocked entity")
    restored(); eq(state.callbacks.disable, 1); eq(state.calls[1].kind, "unblocked")
    local arguments = state.calls[1].arguments
    eq(arguments.n, 5); eq(arguments[1], 8); eq(arguments[2], 300)
    eq(arguments[3], 400); eq(arguments[4], 180); eq(arguments[5], 3)
end)
test("wrappers retain the historical single return including nil and false", function()
    setup(function(id) eq(id, 7); return nil, "unused" end,
        function(id) eq(id, 8); return false, "unused" end)
    local activation = pack(ActivateFireplaceforBanditPack(7))
    local deactivation = pack(DisableFireplaceforBanditPack(8))
    eq(activation.n, 1); eq(activation[1], nil)
    eq(deactivation.n, 1); eq(deactivation[1], false); restored()
end)
test("activation failure restores the creator without retrying gameplay", function()
    setup(function() error("activation failure", 0) end)
    failed(ActivateFireplaceforBanditPack, "activation failure")
    restored(); eq(state.callbacks.activate, 1)
end)
test("deactivation failure restores the creator without retrying gameplay", function()
    setup(nil, function() error("deactivation failure", 0) end)
    failed(DisableFireplaceforBanditPack, "deactivation failure")
    restored(); eq(state.callbacks.disable, 1)
end)
test("callback error objects retain their identity", function()
    local originalError = {kind = "gameplay failure"}
    setup(function() error(originalError, 0) end)
    failed(ActivateFireplaceforBanditPack, originalError); restored()
end)
test("false and nil callback errors remain failures", function()
    setup(function() error(false, 0) end, function() error(nil, 0) end)
    failed(ActivateFireplaceforBanditPack, false); restored()
    failed(DisableFireplaceforBanditPack, nil); restored()
end)
test("initial source and line diagnostic are retained", function()
    setup(function() error("source diagnostic") end)
    local ok, originalError = pcall(S6Patcher.ActivateFireplaceforBanditPack, 7)
    eq(ok, false); assert(originalError:find("hooks_spec.lua:", 1, true))
    failed(ActivateFireplaceforBanditPack, originalError); restored()
end)
test("initial callback traceback is logged after the creator is restored", function()
    setup(function() error("traceback diagnostic", 0) end)
    local originalLogger = Framework.WriteToLog
    Framework.WriteToLog = function(message) restored(); originalLogger(message) end
    failed(ActivateFireplaceforBanditPack, "traceback diagnostic"); restored()
    eq(#state.logs, 1)
    assert(state.logs[1]:find("stack traceback:", 1, true), "initial traceback missing")
    assert(state.logs[1]:find("hooks_spec.lua:", 1, true), "initial callback source missing")
end)
test("an unavailable debug library cannot mask the original callback error", function()
    setup(function() error("original failure", 0) end); debug = nil
    failed(ActivateFireplaceforBanditPack, "original failure"); restored()
end)
test("an unavailable traceback function cannot mask the original callback error", function()
    setup(function() error("original failure", 0) end); debug = {}
    failed(ActivateFireplaceforBanditPack, "original failure"); restored()
end)
test("traceback generation errors cannot mask the original callback error", function()
    local originalError = {kind = "original failure"}
    setup(function() error(originalError, 0) end)
    debug = {traceback = function() error("traceback failure", 0) end}
    failed(ActivateFireplaceforBanditPack, originalError); restored()
end)
test("an unavailable logger cannot mask the original callback error", function()
    setup(nil, function() error("original failure", 0) end); Framework.WriteToLog = nil
    failed(DisableFireplaceforBanditPack, "original failure"); restored()
end)
test("a failing logger cannot undo restoration or mask the original callback error", function()
    setup(function() error("original failure", 0) end); state.logFailure = true
    failed(ActivateFireplaceforBanditPack, "original failure"); restored()
    eq(state.logAttempts, 1)
end)
test("formatting the primary error cannot mask it or skip restoration", function()
    local originalError = setmetatable({}, {__tostring = function() error("format failure", 0) end})
    setup(function() error(originalError, 0) end)
    local ok, message = pcall(ActivateFireplaceforBanditPack, 7)
    eq(ok, false); assert(message == originalError, "primary error identity changed"); restored()
    eq(#state.logs, 1); assert(state.logs[1]:find("<unprintable error>", 1, true))
    assert(state.logs[1]:find("stack traceback:", 1, true))
end)
test("a successful call after a failure restores ordinary entity creation", function()
    local first = true
    setup(function()
        if first then first = false; error("first call failure", 0) end
        return Logic.CreateEntity(7)
    end)
    failed(ActivateFireplaceforBanditPack, "first call failure")
    eq(ActivateFireplaceforBanditPack(7), "unblocked entity"); restored()
    eq(Logic.CreateEntity(8), "original entity"); eq(state.callbacks.activate, 2)
end)
test("twenty successive activation and deactivation calls restore the creator", function()
    setup()
    for i = 1, 20 do
        ActivateFireplaceforBanditPack(i); restored()
        DisableFireplaceforBanditPack(i); restored()
    end
    eq(state.callbacks.activate, 20); eq(state.callbacks.disable, 20)
end)
test("activation containing deactivation restores each scope", function()
    setup(function(id)
        eq(Logic.CreateEntity, state.unblocked)
        eq(DisableFireplaceforBanditPack(id), "unblocked entity")
        eq(Logic.CreateEntity, state.unblocked)
        return "outer activation"
    end)
    eq(ActivateFireplaceforBanditPack(7), "outer activation"); restored()
    eq(state.callbacks.activate, 1); eq(state.callbacks.disable, 1)
end)
test("deactivation containing activation restores each scope", function()
    setup(nil, function(id)
        eq(ActivateFireplaceforBanditPack(id), "unblocked entity")
        eq(Logic.CreateEntity, state.unblocked)
        return "outer deactivation"
    end)
    eq(DisableFireplaceforBanditPack(7), "outer deactivation"); restored()
end)
test("bounded recursive activation restores the creator", function()
    setup(function(depth)
        eq(Logic.CreateEntity, state.unblocked)
        if depth > 0 then eq(ActivateFireplaceforBanditPack(depth - 1), "recursive") end
        eq(Logic.CreateEntity, state.unblocked)
        return "recursive"
    end)
    eq(ActivateFireplaceforBanditPack(3), "recursive"); restored()
    eq(state.callbacks.activate, 4)
end)
test("propagated nested failure restores the outer creator", function()
    local originalError = {kind = "inner failure"}
    setup(function(id) return DisableFireplaceforBanditPack(id) end,
        function() error(originalError, 0) end)
    failed(ActivateFireplaceforBanditPack, originalError); restored()
end)
test("caught inner failure leaves the outer hook until the outer callback returns", function()
    setup(function(id)
        local ok, message = pcall(DisableFireplaceforBanditPack, id)
        eq(ok, false); eq(message, "inner failure")
        eq(Logic.CreateEntity, state.unblocked)
        return Logic.CreateEntity(id)
    end, function() error("inner failure", 0) end)
    eq(ActivateFireplaceforBanditPack(7), "unblocked entity"); restored()
end)
test("recursive failure unwinds all scopes and allows another callback", function()
    setup(function(depth)
        if depth == 0 then error("recursive failure", 0) end
        return ActivateFireplaceforBanditPack(depth - 1)
    end)
    failed(function() return ActivateFireplaceforBanditPack(3) end, "recursive failure")
    restored(); eq(DisableFireplaceforBanditPack(8), "unblocked entity"); restored()
end)
test("script reload keeps the original callbacks without duplicating calls", function()
    setup(); loadScript()
    eq(ActivateFireplaceforBanditPack(7), "unblocked entity"); restored()
    eq(DisableFireplaceforBanditPack(8), "unblocked entity"); restored()
    eq(state.callbacks.activate, 1); eq(state.callbacks.disable, 1)
end)
test("callback changes to the creator cannot leak beyond its scope", function()
    setup(function()
        Logic.CreateEntity = function() return "changed in callback" end
        return "callback result"
    end)
    eq(ActivateFireplaceforBanditPack(7), "callback result"); restored()
end)
test("restoration targets the captured engine table if the global is replaced", function()
    local replacement = {CreateEntity = function() return "replacement" end}
    setup(function() Logic = replacement; error("replaced global", 0) end)
    failed(ActivateFireplaceforBanditPack, "replaced global"); restored()
    eq(Logic, replacement)
end)
test("partially failed hook installation is unwound before error propagation", function()
    setup(nil, nil, true); state.installFailure = "installation failure"
    failed(ActivateFireplaceforBanditPack, "installation failure")
    restored(); eq(state.callbacks.activate, 0)
end)
test("silently ignored hook installation fails before gameplay is called", function()
    setup(nil, nil, true); state.installIgnored = true
    local ok, message = pcall(ActivateFireplaceforBanditPack, 7)
    eq(ok, false); assert(message:find("CreateEntity hook installation failed", 1, true))
    restored(); eq(state.callbacks.activate, 0)
end)
test("restoration failure after successful gameplay is an explicit failure", function()
    setup(nil, nil, true); state.restoreFailure = "restoration failure"
    failed(ActivateFireplaceforBanditPack, "restoration failure")
    eq(state.callbacks.activate, 1)
    state.restoreFailure = nil; Logic.CreateEntity = state.original
    eq(DisableFireplaceforBanditPack(8), "unblocked entity"); restored()
end)
test("silently ignored restoration cannot announce success", function()
    setup(nil, nil, true); state.restoreIgnored = true
    local ok, message = pcall(ActivateFireplaceforBanditPack, 7)
    eq(ok, false); assert(message:find("CreateEntity restoration failed", 1, true))
    eq(state.restoreAttempts, 1)
end)
test("silently ignored restoration is diagnosed without replacing the primary error", function()
    setup(function() error("original failure", 0) end, nil, true)
    state.restoreIgnored = true
    failed(ActivateFireplaceforBanditPack, "original failure")
    eq(#state.logs, 1); assert(state.logs[1]:find("CreateEntity restoration failed", 1, true))
end)
test("double failure preserves the primary error and logs restoration failure", function()
    local originalError = {kind = "primary failure"}
    setup(function() error(originalError, 0) end, nil, true)
    state.restoreFailure = "restoration failure"
    failed(ActivateFireplaceforBanditPack, originalError)
    eq(#state.logs, 1); assert(state.logs[1]:find("restoration failure", 1, true))
end)
test("diagnostic failure cannot replace the original callback error", function()
    setup(nil, function() error("original failure", 0) end, true)
    state.restoreFailure = "restoration failure"; state.logFailure = true
    failed(DisableFireplaceforBanditPack, "original failure")
    eq(state.restoreAttempts, 1); eq(state.logAttempts, 1)
end)
test("formatting a restoration error cannot replace the original callback error", function()
    local restoreError = setmetatable({}, {__tostring = function() error("format failure", 0) end})
    setup(function() error("original failure", 0) end, nil, true)
    state.restoreFailure = restoreError
    failed(ActivateFireplaceforBanditPack, "original failure")
    eq(state.restoreAttempts, 1)
    eq(#state.logs, 1); assert(state.logs[1]:find("<unprintable error>", 1, true))
    assert(state.logs[1]:find("stack traceback:", 1, true))
end)

print(string.format("Hooks: %d tests, %d passed, %d failed", tests, tests - failures, failures))
os.exit(failures == 0 and 0 or 1)
