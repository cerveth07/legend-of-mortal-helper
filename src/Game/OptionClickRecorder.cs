using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    // Observation only: the story recovery recorder needs to know which option was
    // clicked. The native choice flow is untouched.
    internal sealed class OptionClickRecorder : MonoBehaviour, IPointerClickHandler
    {
        private Text source;

        internal static void Attach(Component row, Text text)
        {
            if (row == null || text == null) return;
            var recorder = row.GetComponent<OptionClickRecorder>() ?? row.gameObject.AddComponent<OptionClickRecorder>();
            recorder.source = text;
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (data == null || data.button != PointerEventData.InputButton.Left) return;
            if (source == null) return;
            StoryRecovery.NoteChoice(source.text);
        }
    }
}
