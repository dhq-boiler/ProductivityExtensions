using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Shell;
using Reactive.Bindings;
using System.Globalization;
using System.Linq;
using Reactive.Bindings.Extensions;

namespace boilersExtensions.ViewModels
{
    public class RegisterResourceDialogViewModel : ViewModelBase
    {
        public string SelectedText { get; set; }

        public ReactivePropertySlim<string> ResourceKey { get; } = new ReactivePropertySlim<string>();

        public ReactivePropertySlim<string> SelectedCulture { get; } = new ReactivePropertySlim<string>("default");

        public ReactivePropertySlim<string> ResourceClassName { get; } = new ReactivePropertySlim<string>();

        public ReactivePropertySlim<bool> UseCustomResourceClass { get; } = new ReactivePropertySlim<bool>(false);

        public List<string> AvailableCultures { get; } = new List<string>
        {
            "default",
            "en-US",
            "ja-JP"
            // Add more cultures as needed
        };

        public RegisterResourceDialogViewModel()
        {
            UseCustomResourceClass.Subscribe(useCustom =>
            {
                if (useCustom)
                {
                    // If using a custom class, set the default class name
                    ResourceClassName.Value = "ResourceService";
                }
                else
                {
                    // Reset the class name if not using a custom class
                    ResourceClassName.Value = string.Empty;
                }
            }).AddTo(Disposables);

            // Generate a suggested resource key from the selected text
            // This would be called after SelectedText is set
        }

        public void GenerateSuggestedKey()
        {
            if (string.IsNullOrEmpty(SelectedText))
                return;

            // Generate a PascalCase key from the first few words
            string[] words = SelectedText.Split(' ', '.', ',', ':', ';', '!', '?', '\r', '\n');
            string key = string.Join("", words.Take(3)
                .Where(w => !string.IsNullOrEmpty(w))
                .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));

            // Limit length and remove invalid characters
            key = new string(key.Take(50).Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

            if (string.IsNullOrEmpty(key))
                key = "Resource" + System.DateTime.Now.Ticks.ToString().Substring(0, 5);

            ResourceKey.Value = key;
        }
    }
}