using System.Collections.Generic;
using System.Linq;
using DhcpServer.App.Localization;
using DhcpServer.Core.Addressing;

namespace DhcpServer.App.ViewModels
{
    /// <summary>Asks the user for consent before anything is changed or discarded.</summary>
    public interface IUserDialogs
    {
        /// <summary>Shows the current/new comparison; true only when the user explicitly confirms.</summary>
        bool ConfirmChanges(ChangePreview preview, string adapterName);

        /// <summary>Asks whether the saved settings may be thrown away (they will not be restored).</summary>
        bool ConfirmDiscard();
    }

    public sealed class ConfirmRowViewModel
    {
        public ConfirmRowViewModel(string label, string current, string proposed, bool changed)
        {
            Label = label;
            Current = current;
            New = proposed;
            Changed = changed;
        }

        public string Label { get; }

        public string Current { get; }

        public string New { get; }

        public bool Changed { get; }
    }

    /// <summary>Content of the confirmation window: a localized table built from the Core preview.</summary>
    public sealed class ConfirmChangesViewModel
    {
        public ConfirmChangesViewModel(ChangePreview preview, string adapterName)
        {
            Intro = LanguageService.Format("Confirm_Intro", adapterName);
            Rows = preview.Rows
                .Select(r => new ConfirmRowViewModel(LanguageService.Get(r.LabelKey), Text(r.Current), Text(r.New), r.Changed))
                .ToList();
        }

        public string Intro { get; }

        public IReadOnlyList<ConfirmRowViewModel> Rows { get; }

        private static string Text(PreviewValue value)
        {
            switch (value.Kind)
            {
                case PreviewKind.Automatic:
                    return LanguageService.Get("Confirm_Automatic");
                case PreviewKind.Static:
                    return LanguageService.Get("Confirm_Static");
                case PreviewKind.None:
                    return LanguageService.Get("Confirm_None");
                default:
                    return value.Text;
            }
        }
    }
}
