using System.Collections.Generic;
using boilersExtensions.Helpers;
using Microsoft.VisualStudio.Shell;

namespace boilersExtensions.Utils
{
    public static class MenuTextUpdater
    {
        // List of all commands that need text updates
        private static readonly List<OleMenuCommand> _registeredCommands = new List<OleMenuCommand>();

        // Register a command to be updated when language changes
        public static void RegisterCommand(OleMenuCommand command, string resourceKey)
        {
            command.AutomationName = resourceKey; // Store the resource key in the AutomationName property
            _registeredCommands.Add(command);

            // Update the command text with current language
            UpdateCommandText(command);
        }

        // Update all registered commands
        public static void UpdateAllCommandTexts()
        {
            foreach (var command in _registeredCommands)
            {
                UpdateCommandText(command);
            }
        }

        // Update a single command's text
        private static void UpdateCommandText(OleMenuCommand command)
        {
            if (command.AutomationName is string resourceKey)
            {
                command.Text = ResourceService.GetString(resourceKey);
            }
        }
    }
}