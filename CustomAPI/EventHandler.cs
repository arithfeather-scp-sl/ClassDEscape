using MEC;
using RemoteAdmin;
using Smod2;
using Smod2.API;
using Smod2.EventHandlers;
using Smod2.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ArithFeather.CustomAPI
{
    internal class EventHandler : IEventHandlerWaitingForPlayers, IEventHandlerLateDisconnect, IEventHandlerRoundRestart
	{
		private Plugin plugin;
		public EventHandler(Plugin plugin)
		{
			this.plugin = plugin;
			CustomPlayerManager = new CustomPlayerManager(plugin);
			LightControl = new LightControl(plugin);
			Broadcasts = new Broadcasts(plugin);
			ManualEndGame = new ManualEndGame(plugin);
		}

		public CustomPlayerManager CustomPlayerManager;
		public LightControl LightControl;
		public Broadcasts Broadcasts;
		public ManualEndGame ManualEndGame;

		public void OnLateDisconnect(LateDisconnectEvent ev) => CustomPlayerManager.DisconnectEvent();

		public void OnWaitingForPlayers(WaitingForPlayersEvent ev)
		{
			LightControl.WaitingForPlayersEvent();
			Broadcasts.UpdateBroadcast();
		}

		public void OnRoundRestart(RoundRestartEvent ev)
		{
			LightControl.RoundRestartEvent();
			ManualEndGame.RoundRestartEvent();
		}
	}
}
