using System;

namespace MortalHelper.Core
{
    // How a failed recovery must leave the game. The lock exists so a half-restored script
    // cannot be played or saved over, but it must never outlive the recovery attempt: a
    // failure that keeps input disabled makes the game inoperable and is worse than the
    // original problem. Pure decisions, so the policy is testable without a game.
    public static class FailureExit
    {
        // Give the failed frame a moment to settle before asking for the title screen.
        public const float TitleDelay = 0.75f;
        // Hard stop: after this the input lock is released no matter what the scene is doing,
        // because refusing to release it is what leaves a player unable to quit or continue.
        public const float InputReleaseLimit = 8f;
        // A load that never produces its scene would otherwise hold the lock until the
        // replay own two-minute stall watchdog fires; bound it far earlier.
        public const float SceneReadyLimit = 45f;
        // The scene controller can sit in IsLoading forever. Past this the load is treated as
        // failed even though it still claims to be loading.
        public const float LoadStallHardLimit = 90f;

        // Ask for the title at most once, and only once the scene is stable. Requesting a
        // scene change while a load is still running is not a recovery, it is a second
        // transition on top of a broken one. A released input lock never cancels this: a
        // stopped recovery can leave a scene that cannot be played, while the lock is what
        // made it unresponsive, so releasing input and returning to the title can both happen.
        public static bool ShouldRequestTitle(bool loading, bool alreadyRequested, float waited)
        {
            return !alreadyRequested && !loading && waited >= TitleDelay;
        }

        // True once the input lock has been held too long for any legitimate exit.
        public static bool ShouldReleaseInput(float waited)
        {
            return waited >= InputReleaseLimit;
        }

        // The replay may only start arriving after the scene is up; if it never does, the
        // attempt is over. Measured from the load request, not from user input.
        public static bool ShouldAbortForMissingScene(bool sceneReady, float waited)
        {
            return !sceneReady && waited >= SceneReadyLimit;
        }

        // The original stall window for a controller that is idle but whose scene never came.
        public const float NativeLoadIdleLimit = 60f;

        // A load that claims to be loading forever is not going to finish either, but it gets
        // longer than an idle controller: data application on a slow machine is not a stall.
        public static bool LoadStalled(bool loading, float waited)
        {
            return loading ? waited >= LoadStallHardLimit : waited >= NativeLoadIdleLimit;
        }

        // A request that threw cannot be retried safely; the caller must degrade instead.
        public static string FailReason(string reason)
        {
            return string.IsNullOrEmpty(reason) ? "恢复失败" : reason;
        }
    }
}
