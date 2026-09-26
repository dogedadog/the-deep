using System.Text;
using TheDeep.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace TheDeep.UI.Terminal.Apps
{
    /// <summary>
    /// The investigation into the missing Dive Team 7: every piece of evidence the crew has ever
    /// submitted. Saved permanently in the host's slot. Footage arrives with body recovery (step 7).
    /// </summary>
    public class CaseFilesApp : TerminalApp
    {
        Text list;
        bool open;

        public override string Title => "Case Files";
        public override string IconGlyph => "FILE";
        public override Color IconColor => new(0.35f, 0.3f, 0.35f);
        public override Vector2 WindowSize => new(560, 420);

        public override void BuildContent(RectTransform content)
        {
            float width = WindowSize.x - 16;
            var header = RetroUI.Label("Header", content, "CASE 0047-B:  DIVE TEAM 7  (STATUS: MISSING)", 16, style: FontStyle.Bold);
            RetroUI.Place(header.rectTransform, 0, 0, width, 24);
            var note = RetroUI.Label("Note", content,
                "Four divers lost at this site. No distress call. Recover anything that belonged to them.", 13);
            RetroUI.Place(note.rectTransform, 0, 24, width, 36);
            list = RetroUI.Readout("Files", content, "", 14);
            RetroUI.Place((RectTransform)list.transform.parent, 0, 64, width, WindowSize.y - 64 - 56);
            Refresh();
        }

        public override void OnOpened()
        {
            open = true;
            Refresh();
        }

        public override void OnClosed() => open = false;

        void Update()
        {
            if (open && Time.frameCount % 30 == 0) Refresh();
        }

        void Refresh()
        {
            var progress = CrewProgress.Instance;
            if (progress == null || !progress.IsSpawned)
            {
                list.text = "RECORDS OFFLINE";
                return;
            }
            if (progress.CaseFiles.Count == 0)
            {
                list.text = "> NO EVIDENCE RECOVERED\n\n> Look for personal effects on the seabed.\n> Evidence is filed when its data is submitted.";
                return;
            }
            var sb = new StringBuilder();
            for (int i = 0; i < progress.CaseFiles.Count; i++)
            {
                var file = progress.CaseFiles[i];
                sb.Append($"#{i + 1:00}  {file.Title}\n     recovered on expedition #{file.Expedition}\n\n");
            }
            list.text = sb.ToString();
        }
    }
}
