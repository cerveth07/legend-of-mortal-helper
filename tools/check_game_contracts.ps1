# Read-only contract checks against a user's installed game. Does not start Unity.
param([Parameter(Mandatory=$true)][string]$Game)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $Game 'BepInEx/core/Mono.Cecil.dll')
$assemblies = @{}
foreach ($name in @('Mortal.Battle','Mortal.Core','Mortal.Story','Mortal.Combat','Mortal.Free','Fungus')) {
    $assemblies[$name] = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $Game "Mortal_Data/Managed/$name.dll"))
}
$count = 0
function Assert($ok, $message) {
    if (!$ok) { throw "FAIL: $message" }
    $script:count++
    Write-Output "PASS: $message"
}
function Get-GameType($assembly, $name) { return $assemblies[$assembly].MainModule.Types | Where-Object Name -eq $name }
function Get-GameMethod($type, $name) { return $type.Methods | Where-Object Name -eq $name }
function Operands($method) { return @($method.Body.Instructions | ForEach-Object { "$($_.Operand)" }) }
try {
    $manager = Get-GameType 'Mortal.Battle' 'GameLevelManager'
    $timer = $manager.NestedTypes | Where-Object Name -like '<StartSessionTime>*'
    Assert (@($timer).Count -eq 1) 'unique native session timer state machine'
    $move = Get-GameMethod $timer 'MoveNext'
    $writes = @($move.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and "$($_.Operand)" -eq 'System.Int32 Mortal.Battle.GameLevelManager::_currentSessionTime' })
    Assert ($writes.Count -eq 1) 'freeze transpiler has exactly one elapsed assignment'
    $ops = Operands $move
    Assert (($ops -contains 'System.Int32 Mortal.Battle.GameLevelManager::_maxSessionTime') -and ($ops -contains 'System.Void Mortal.Battle.BattleLevel::ExecuteEvent(System.Int32,UnityEngine.MonoBehaviour)')) 'elapsed timer owns timeout and timed battle events'
    $button = Get-GameType 'Mortal.Battle' 'SkillButton'
    $gate = Operands (Get-GameMethod $button 'CanProcess')
    foreach ($property in @('IsPause','IsReady','IsGameOver','IsEventPlaying')) {
        Assert ($gate -contains "System.Boolean Mortal.Battle.GameLevelManager::get_$property()") "native skill gate preserves $property"
    }
    $update = Operands (Get-GameMethod $button 'Update')
    Assert (($update -contains 'System.Single Mortal.Battle.SkillButton::_currentCooldownTime') -and ($update -contains 'System.Boolean Mortal.Battle.SkillButton::CanProcess()')) 'cooldown update checks native gate and float cooldown field'
    $martial = Get-GameType 'Mortal.Core' 'MartialPanel'
    $learn = Operands (Get-GameMethod $martial 'CanUseLearnButton')
    Assert (($learn -contains 'System.Boolean Mortal.Core.MartialLevelData::Check()') -and ($learn -contains 'System.Int32 Mortal.Core.GameStat::get_FinalValue()') -and ($learn -contains 'System.Int32 Mortal.Core.MartialPanel::_currentCount')) 'learning eligibility checks conditions, available points and practice cap'
    foreach ($name in @('AddFlag','SetFlag')) {
        $method = Get-GameMethod (Get-GameType 'Mortal.Core' 'MissionManagerData') $name
        Assert (($method.Parameters.Count -eq 2) -and ($method.Parameters[0].ParameterType.FullName -eq 'System.String') -and ($method.Parameters[1].ParameterType.FullName -eq 'System.Int32')) "$name binds flag name to __0 and value to __1"
    }
    $input = Operands (Get-GameMethod (Get-GameType 'Mortal.Story' 'DiceMenuDialog') 'GetPressInput')
    Assert ($input -contains 'System.Boolean Mortal.Core.SystemSettings::get_ManualDestiny()') 'native dice input supports automatic destiny'
    $load = Get-GameMethod (Get-GameType 'Mortal.Core' 'SaveSystem') 'ExecuteLoadGameData'
    Assert (($load.Parameters.Count -eq 1) -and ($load.Parameters[0].ParameterType.FullName -eq 'Mortal.Core.GameSave')) 'native load boundary receives GameSave as __0'
    $scene = Get-GameMethod (Get-GameType 'Mortal.Core' 'SceneController') 'StartLoadScene'
    Assert (($scene.ReturnType.FullName -eq 'System.Collections.IEnumerator') -and ($scene.Parameters[0].ParameterType.FullName -eq 'System.String')) 'scene load observer receives scene name and native IEnumerator'
    $player = Get-GameType 'Mortal.Core' 'PlayerStatManagerData'
    $saveOps = Operands (Get-GameMethod $player 'Save')
    Assert (($saveOps -contains 'UnityEngine.Random/State UnityEngine.Random::get_state()') -and ($saveOps -contains 'Mortal.Core.UnityRandomState Mortal.Core.GameSave::FreeRandomState')) 'native save snapshot captures live Unity random state'
    $loadOps = Operands (Get-GameMethod $player 'Load')
    Assert (($loadOps -contains 'Mortal.Core.UnityRandomState Mortal.Core.PlayerStatManagerData::_freeRandomState') -and !($loadOps -contains 'System.Void UnityEngine.Random::set_state(UnityEngine.Random/State)')) 'native load stores free random seed without restoring Unity RNG'
    $talent = Get-GameType 'Mortal.Core' 'PlayerTalentData'
    Assert (@($talent.Fields | Where-Object { $_.Name -eq '_maxLevel' -and $_.FieldType.FullName -eq 'System.Int32' }).Count -eq 1) 'talent editor reads a native integer maximum'
    $setup = Get-GameMethod $talent 'Setup'
    Assert (($setup.Parameters.Count -eq 1) -and ($setup.Parameters[0].ParameterType.FullName -eq 'System.Int32') -and ((Operands $setup) -contains 'System.Int32 Mortal.Core.PlayerTalentData::_level')) 'talent Setup writes native level'
    Assert (($saveOps -contains 'System.Int32 Mortal.Core.TalentSave::Level') -and ($loadOps -contains 'System.Void Mortal.Core.PlayerTalentDataCollection::Set(System.String,System.Int32)')) 'native saves persist talent levels and reload them'
    $missions = Get-GameType 'Mortal.Core' 'MissionManagerData'
    $flagSave = Operands (Get-GameMethod $missions 'Save')
    $flagLoad = Operands (Get-GameMethod $missions 'Load')
    Assert (($flagSave -contains 'System.Int32 Mortal.Core.FlagData::State') -and ($flagSave -contains 'System.Int32 Mortal.Core.EventFlagSave::State') -and ($flagLoad -contains 'System.Void Mortal.Core.MissionManagerData::SetFlag(System.String,System.Int32)')) 'native saves persist faction flag state and reload it'
    $sourceRoot = Join-Path $PSScriptRoot '../src'
    $targets = @()
    foreach ($file in Get-ChildItem $sourceRoot -Recurse -Filter '*.cs') {
        $text = Get-Content $file.FullName -Raw
        # Every HarmonyPatch attribute that names its target method as a string must be
        # resolvable here. Extra attribute arguments are allowed (a patch may pin the
        # overload by argument type), but an attribute this extractor cannot read is an
        # error: silently skipping one is how SaveSystem.SaveGameData went unchecked.
        $declared = [regex]::Matches($text, '\[HarmonyPatch\(typeof\(').Count
        $classOnly = [regex]::Matches($text, '\[HarmonyPatch\(typeof\([A-Za-z0-9_]+\)\)\]').Count
        $named = [regex]::Matches($text, '\[HarmonyPatch\(typeof\(([A-Za-z0-9_]+)\)\s*,\s*"([A-Za-z0-9_]+)"')
        if ($named.Count + $classOnly -ne $declared) {
            throw ("Unresolved HarmonyPatch attribute in " + $file.Name + ": the extractor reads " +
                $named.Count + " named and " + $classOnly + " class-only of " + $declared +
                " attributes. Extend the extractor instead of letting a target go unchecked.")
        }
        foreach ($match in $named) {
            $targets += [pscustomobject]@{ Type = $match.Groups[1].Value; Method = $match.Groups[2].Value }
        }
    }
    foreach ($name in @('PressStartButton','PressRestartButton','RestartButtonClick','DestinyRestartAction','NextInputAction','DestinyAction','RangeAction')) {
        $targets += [pscustomobject]@{ Type = 'DiceMenuDialog'; Method = $name }
    }
    foreach ($target in $targets) {
        $found = @($assemblies.Values | ForEach-Object { $_.MainModule.Types } | Where-Object Name -eq $target.Type | ForEach-Object { $_.Methods } | Where-Object Name -eq $target.Method)
        if ($found.Count -eq 0) { throw "Missing patch target: $($target.Type).$($target.Method)" }
    }
    Write-Output "Harmony targets resolved: $($targets.Count) named targets plus verified timer MoveNext."
    Write-Output "Game metadata contracts: $count passed. Live gameplay acceptance is still required."
}
finally { foreach ($assembly in $assemblies.Values) { $assembly.Dispose() } }
