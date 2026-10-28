using System;
using System.IO;
using MortalHelper.Core;

internal static class CheckpointCompatibilityContracts
{
    private sealed class Model { public string Version = ""; public int Value = 0; public Flag[] EventFlags = new Flag[0]; }
    private sealed class Flag { public string Name = ""; public int State = 0; }
    public static void Run(Action<bool, string> check)
    {
        string old = "{\"Version\":\"old\",\"TimeTick\":1,\"Value\":3,\"EventFlags\":[{\"Name\":\"A\",\"State\":2}]}";
        string defaults = "{\"Version\":\"new\",\"TimeTick\":2,\"Value\":0,\"NewFeature\":0,\"StoryLogs\":[],\"EventFlags\":[{\"Name\":\"B\",\"State\":0},{\"Name\":\"A\",\"State\":0}]}";
        string live = defaults.Replace("\"Value\":0", "\"Value\":3").Replace("\"A\",\"State\":0", "\"A\",\"State\":2");
        check(CheckpointCompatibility.Equivalent(old, defaults, live), "major version labels, additions and stable-id ordering can migrate");
        check(!CheckpointCompatibility.Equivalent(old, defaults, live.Replace("\"Value\":3", "\"Value\":4")), "recorded gameplay value must not drift");
        check(!CheckpointCompatibility.Equivalent(old, defaults, live.Replace("\"B\",\"State\":0", "\"B\",\"State\":1")), "unrecorded new flags must retain native baseline defaults");
        check(!CheckpointCompatibility.Equivalent(old, defaults, live.Replace("\"NewFeature\":0", "\"NewFeature\":1")), "new gameplay fields cannot silently change");
        check(CheckpointCompatibility.Equivalent(old, defaults, live.Replace("\"StoryLogs\":[]", "\"StoryLogs\":[{\"Text\":\"new presentation\"}]")), "new presentation history is distinct from recorded gameplay");
        check(!CheckpointCompatibility.Equivalent("{\"StoryLogs\":[1]}", "{\"StoryLogs\":[]}", "{\"StoryLogs\":[2]}"), "already recorded history stays checked");
        check(!CheckpointCompatibility.Equivalent("{\"Rng\":[1,2]}", "{\"Rng\":[0,0]}", "{\"Rng\":[2,1]}"), "random-state arrays remain ordered and exact");
        check(!CheckpointCompatibility.Equivalent("{\"V\":1.0000000000000001}", "{\"V\":0}", "{\"V\":1.0000000000000002}"), "numeric state has no rounding tolerance");
        foreach (Action invalid in new Action[] {
            () => CheckpointCompatibility.ValidateSchema("{\"Removed\":1}", typeof(Model)),
            () => CheckpointCompatibility.ValidateSchema("{\"Value\":\"3\"}", typeof(Model)),
            () => CheckpointCompatibility.ValidateSchema("{\"Value\":2147483648}", typeof(Model)),
            () => CheckpointCompatibility.ValidateSchema("{\"Value\":null}", typeof(Model)),
            () => CheckpointCompatibility.ValidateSchema("{\"Value\":{}}", typeof(Model)),
            () => CheckpointCompatibility.ValidateSchema("{\"Version\":{}}", typeof(Model)),
            () => CheckpointCompatibility.ValidateSchema("{\"EventFlags\":[{\"Name\":\"A\",\"Removed\":2}]}", typeof(Model)),
            () => CheckpointCompatibility.Equivalent(old, "{\"Value\":0,\"EventFlags\":[]}", live),
            () => CheckpointCompatibility.Equivalent("{\"V\":1,\"V\":2}", "{}", "{}"),
            () => CheckpointCompatibility.Equivalent("{\"Value\":01}", "{}", "{}"),
            () => CheckpointCompatibility.Equivalent("{\"Value\":NaN}", "{}", "{}"),
            () => CheckpointCompatibility.Equivalent("{\"EventFlags\":[{\"Name\":\"A\",\"State\":0},{\"Name\":\"A\",\"State\":1}]}", defaults, live)
        })
        {
            bool rejected = false; try { invalid(); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "schema loss, ambiguous ids and malformed documents are refused");
        }
        CheckpointCompatibility.ValidateSchema("{\"Version\":\"old\",\"Value\":3,\"EventFlags\":[{\"Name\":\"A\",\"State\":2}]}", typeof(Model));
        check(true, "valid recorded nested schema accepted");
        check(CheckpointCompatibility.Equivalent("{\"EventFlags\":[{\"Name\":\"A\",\"State\":2},{\"Name\":\"A\",\"State\":2}]}",
            "{\"EventFlags\":[{\"Name\":\"A\",\"State\":0}]}", "{\"EventFlags\":[{\"Name\":\"A\",\"State\":2}]}"), "identical native flag aliases can migrate");
    }
}
