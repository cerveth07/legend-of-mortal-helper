namespace MortalHelper.Recovery
{
    // The game side of a replay. Deliberately small: the adapter loads the native
    // baseline, reports one canonical state summary, and takes control back when
    // replay stops. None of these members are implemented or verified in game yet.
    public interface IReplayHost
    {
        // Identifies the game build the recording was made on.
        string Client { get; }

        // Loads the native save the events replay on top of. The payload format
        // belongs to the adapter that produced the baseline.
        void LoadBaseline(string baseline);

        // A stable summary of every value that influences later story state.
        string CaptureState();

        // Called once when replay stops for any reason. The adapter must stop
        // injecting recorded inputs and leave the game controllable.
        void Abort(string reason);
    }
}
