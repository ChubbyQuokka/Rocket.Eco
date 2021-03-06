﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Rocket.API.Commands;
using Rocket.API.Player;
using Rocket.API.User;
using Rocket.Core.Commands;
using Rocket.Eco.Player;

namespace Rocket.Eco.Commands.EcoCommands
{
    /// <inheritdoc />
    /// <summary>
    ///     A command to override the vanilla kick command to allow the console to execute it.
    /// </summary>
    public sealed class CommandBan : ICommand
    {
        /// <inheritdoc />
        public bool SupportsUser(IUser user) => true;

        /// <inheritdoc />
        public string Name => "Ban";

        /// <inheritdoc />
        public string[] Aliases => new string[0];

        /// <inheritdoc />
        public string Summary => "Bans a player.";

        /// <inheritdoc />
        public string Description => null;

        /// <inheritdoc />
        public string Syntax => "[[n]ame / id] <reason>";

        /// <inheritdoc />
        public IChildCommand[] ChildCommands => new IChildCommand[0];

        /// <inheritdoc />
        public async Task ExecuteAsync(ICommandContext context)
        {
            if (context.Parameters.Length == 0)
                throw new CommandWrongUsageException();

            IPlayerManager playerManager = context.Container.Resolve<IPlayerManager>("eco");

            IUser userInfo;

            if (playerManager.TryGetOnlinePlayer(context.Parameters[0], out IPlayer onlinePlayer))
            {
                userInfo = ((EcoPlayer) onlinePlayer).User;
            }
            else
            {
                IPlayer player = await playerManager.GetPlayerAsync(context.Parameters[0]);

                switch (player)
                {
                    case EcoPlayer ecoPlayer:
                        userInfo = ecoPlayer.User;
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Eco's IPlayerManager returned an invalid player! This can only happen if a plugin overrides it.");
                }
            }

            string reason = null;

            if (context.Parameters.Length > 1)
                reason = string.Join(" ", context.Parameters.Skip(1));

            await playerManager.BanAsync(userInfo, context.User, reason);

            await context.User.UserManager.SendMessageAsync(null, context.User, "The requested user has been banned.");
        }
    }
}