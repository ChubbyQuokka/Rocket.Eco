﻿using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Eco.Gameplay.Systems.Chat;
using Rocket.API.Commands;
using Rocket.API.Logging;
using Rocket.API.User;
using Rocket.Core.Logging;
using Rocket.Eco.Player;

namespace Rocket.Eco.Commands
{
    /// <inheritdoc cref="ICommand" />
    /// <summary>
    ///     A Rocket representation of a command provied by Eco or its modkit.
    /// </summary>
    public sealed class EcoNativeCommand : ICommand
    {
        private static ChatManager ecoChatManager;
        private static MethodInfo execute;

        private readonly ChatCommandAttribute command;
        private readonly MethodInfo commandMethod;
        private readonly ILogger logger;

        internal EcoNativeCommand(MethodInfo method, ILogger logger)
        {
            this.logger = logger;

            if (ecoChatManager == null)
                ecoChatManager = (ChatManager) typeof(ChatServer)
                                               .GetField("netChatManager",
                                                   BindingFlags.Instance | BindingFlags.NonPublic)
                                               ?.GetValue(ChatServer.Obj)
                    ?? throw new Exception(
                        "A critical part of the Eco codebase has been changed; please uninstall Rocket until it is updated to support these changes.");

            if (execute == null)
                execute = typeof(ChatManager).GetMethod("InvokeCommand", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new Exception(
                        "A critical part of the Eco codebase has been changed; please uninstall Rocket until it is updated to support these changes.");

            command = (ChatCommandAttribute) method.GetCustomAttributes()
                                                   .FirstOrDefault(x => x is ChatCommandAttribute);

            if (command != null)
            {
                commandMethod = method;
                logger.LogInformation($"The vanilla command \"{Name}\" has been registered.");
            }
            else
            {
                logger.LogError(
                    $"An attempt was made to register a vanilla command (method: {method.DeclaringType?.FullName ?? "UnknownType"}.{method.Name}) with inproper attributes!");
            }
        }

        /// <inheritdoc />
        public string[] Aliases => new string[0];

        /// <inheritdoc />
        public string Summary => Description;

        /// <inheritdoc />
        public IChildCommand[] ChildCommands => new IChildCommand[0];

        /// <inheritdoc />
        public string Name => command.UseMethodName ? commandMethod.Name : command.CommandName;

        /// <inheritdoc />
        public string Description => command.HelpText;

        //TODO: Make this match the parameter list of `commandMethod`
        /// <inheritdoc />
        public string Syntax => string.Empty;

        /// <inheritdoc />
        public bool SupportsUser(IUser type) => type is EcoPlayerUser;

        /// <inheritdoc />
        public Task ExecuteAsync(ICommandContext context)
        {
            string args = string.Join(",", context.Parameters);

            try
            {
                execute.Invoke(ecoChatManager,
                    new object[] {Name, commandMethod, args, ((EcoPlayerUser) context.User).Player.InternalEcoUser});
            }
            catch (Exception e)
            {
                logger.LogError($"{context.User.DisplayName} failed to execute the vanilla command `{Name}`!", e);
            }

            return Task.CompletedTask;
        }
    }
}