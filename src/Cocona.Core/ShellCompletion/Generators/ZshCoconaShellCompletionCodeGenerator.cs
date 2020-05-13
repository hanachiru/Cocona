using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Cocona.Application;
using Cocona.Command;
using Cocona.ShellCompletion.Candidate;

namespace Cocona.ShellCompletion.Generators
{
    /// <summary>
    /// Generates the shell completion code for Zsh.
    /// </summary>
    public class ZshCoconaShellCompletionCodeGenerator : ICoconaShellCompletionCodeGenerator
    {
        private readonly string _appName;
        private readonly string _appCommandName;
        private readonly ICoconaCompletionCandidates _completionCandidates;

        public IReadOnlyList<string> Targets { get; } = new[] {"zsh"};

        public ZshCoconaShellCompletionCodeGenerator(
            ICoconaApplicationMetadataProvider applicationMetadataProvider,
            ICoconaCompletionCandidates completionCandidates
        )
        {
            _appName = Regex.Replace(applicationMetadataProvider.GetProductName(), "[^a-zA-Z0-9_]", "__");
            _appCommandName = applicationMetadataProvider.GetExecutableName();
            _completionCandidates = completionCandidates;
        }

        public void Generate(TextWriter writer, CommandCollection commandCollection)
        {
            writer.WriteLine($"#!/bin/zsh");
            writer.WriteLine($"#compdef {_appCommandName}");
            writer.WriteLine($"# ");
            writer.WriteLine($"# Generated by Cocona {nameof(ZshCoconaShellCompletionCodeGenerator)}");
            writer.WriteLine($"# ");
            writer.WriteLine();

            WriteRootCommandDefinition(writer, commandCollection);

            writer.WriteLine($"__cocona_{_appName}_onthefly() {{");
            writer.WriteLine($"    local -a items; items=(${{(f)\"$(\"${{exec_command}}\" --completion-candidates \"zsh:$1\" \"${{words[@]}}\")\"}})");
            writer.WriteLine($"    _describe 'items' items");
            writer.WriteLine($"}}");

            writer.WriteLine();
            writer.WriteLine($"_{_appCommandName}() {{");
            writer.WriteLine($"    __cocona_{_appName}_commands_root");
            writer.WriteLine($"}}");
            writer.WriteLine($"#compdef __cocona_{_appName}_commands_root '{_appCommandName}'");
        }

        public void GenerateOnTheFlyCandidates(TextWriter writer, IReadOnlyList<CompletionCandidateValue> values)
        {
            foreach (var value in values)
            {
                writer.WriteLine($"{value.Value}:{value.Description}");
            }
        }

        private void WriteRootCommandDefinition(TextWriter writer, CommandCollection commandCollection)
        {
            var subCommands = commandCollection.All.Where(x => !x.IsHidden && !x.IsPrimaryCommand).ToArray();

            writer.WriteLine($"__cocona_{_appName}_commands_root() {{");
            writer.WriteLine($"    local exec_command; exec_command=\"${{words[1]}}\"");
            if (commandCollection.Primary != null)
            {
                WriteZshArguments(writer, "root", commandCollection.Primary, subCommands);
            }
            writer.WriteLine("}");
            writer.WriteLine();

            // sub-commands
            foreach (var subCommand in subCommands)
            {
                WriteCommandDefinition(writer, $"root_{subCommand.Name}", subCommand);
            }
        }

        private void WriteCommandDefinition(TextWriter writer, string commandName, CommandDescriptor command)
        {
            var subCommands = command.SubCommands?.All.Where(x => !x.IsHidden && !x.IsPrimaryCommand).ToArray() ?? Array.Empty<CommandDescriptor>();

            writer.WriteLine($"__cocona_{_appName}_commands_{commandName}() {{");
            WriteZshArguments(writer, commandName, command, subCommands);
            writer.WriteLine("}");
            writer.WriteLine();

            foreach (var subCommand in subCommands)
            {
                WriteCommandDefinition(writer, $"{commandName}_{subCommand.Name}", subCommand);
            }
        }

        private void WriteZshArguments(TextWriter writer, string commandName, CommandDescriptor command, CommandDescriptor[] subCommands)
        {
            writer.WriteLine($"    local -a commands");
            writer.WriteLine($"    commands=(");
            foreach (var subCommand in subCommands)
            {
                writer.WriteLine($"        '{subCommand.Name}:{subCommand.Description}'");
            }
            writer.WriteLine($"    )");

            writer.WriteLine($"    _arguments -n -s : \\");
            foreach (var option in command.Options.Where(x => !x.IsHidden))
            {
                if (option.OptionType == typeof(bool))
                {
                    writer.WriteLine($"        '{GetOptions(option)}[{option.Description}]' \\");
                }
                else
                {
                    writer.WriteLine($"        '{GetOptions(option)}[{option.Description}]: :{GetArgumentValues(option)}' \\");
                }
            }
            foreach (var arg in command.Arguments)
            {
                writer.WriteLine($"        '{arg.Order}:{arg.Name}:{GetArgumentValues(arg)}' \\");
            }

            if (subCommands.Any())
            {
                writer.WriteLine($"         \"1: :{{_describe 'command' commands}}\" \\");
                writer.WriteLine($"        '*:: :->args'");

                writer.WriteLine();
                writer.WriteLine($"        case $state in");
                writer.WriteLine($"            args)");
                writer.WriteLine($"                case $words[1] in");
                foreach (var subCommand in subCommands)
                {
                    writer.WriteLine($"                    {subCommand.Name}) __cocona_{_appName}_commands_{commandName}_{subCommand.Name};;");
                }
                writer.WriteLine($"                esac");
                writer.WriteLine($"                ;;");
                writer.WriteLine($"        esac");
            }
            else
            {
                writer.WriteLine($"        #");
            }
        }

        private string GetOptions(CommandOptionDescriptor option)
        {
            if (option.ShortName.Any())
            {
                return $"{(option.IsEnumerableLike ? "*" : "")}'{{--{option.Name},{string.Join(",", option.ShortName.Select(x => "-" + x))}}}'";

            }
            else
            {
                return $"{(option.IsEnumerableLike ? "*" : "")}--{option.Name}";
            }
        }

        private string GetArgumentValues(CommandOptionDescriptor option)
        {
            return GetArgumentValuesCore(_completionCandidates.GetStaticCandidatesFromOption(option), option.Name);
        }
        private string GetArgumentValues(CommandArgumentDescriptor argument)
        {
            return GetArgumentValuesCore(_completionCandidates.GetStaticCandidatesFromArgument(argument), argument.Name);
        }

        private string GetArgumentValuesCore(StaticCompletionCandidates candidates, string name)
        {
            if (candidates.IsOnTheFly)
            {
                // NOTE: See "_arguments/specs: actions" of man 1 zshcompsys
                // "If the action starts with a space, the remaining list of words will be invoked unchanged."
                return $" __cocona_{_appName}_onthefly {name}";
            }
            else
            {
                return candidates.Result!.ResultType switch
                {
                    CompletionCandidateResultType.Default
                    => "_files",
                    CompletionCandidateResultType.File
                    => "_files",
                    CompletionCandidateResultType.Directory
                    => "_path_files -/",
                    CompletionCandidateResultType.Keywords
                    => $"({string.Join(" ", candidates.Result!.Values.Select(x => x.Value))})",
                    _
                    => "_files",
                };
            }
        }
    }

}
