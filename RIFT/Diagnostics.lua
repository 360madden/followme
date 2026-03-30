-- FollowMe Diagnostics | v0.1.0 | 385 chars
FollowMe = FollowMe or {}
FollowMe.Diagnostics = {}

function FollowMe.Diagnostics.Log(message)
  local formatted = "[FollowMe] " .. tostring(message)

  if Command ~= nil and Command.Console ~= nil and Command.Console.Display ~= nil then
    Command.Console.Display("general", true, "<font color=\"#64D2FF\">" .. formatted .. "</font>", true)
    return
  end

  print(formatted)
end

-- FollowMe Diagnostics | v0.1.0 | END
