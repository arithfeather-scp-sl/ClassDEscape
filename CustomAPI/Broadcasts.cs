using Smod2;
using Smod2.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ArithFeather.CustomAPI
{
	internal class Broadcasts
	{
		private Plugin plugin;
		public Broadcasts(Plugin plugin) => this.plugin = plugin;

		private Broadcast cachedBroadcast;

		public void UpdateBroadcast() => cachedBroadcast = GameObject.Find("Host").GetComponent<Broadcast>();

		public void PersonalClearBroadcasts(Player player)
		{
			var connection = (player.GetGameObject() as GameObject).GetComponent<NicknameSync>().connectionToClient;

			if (connection == null)
			{
				return;
			}

			cachedBroadcast.CallTargetClearElements(connection);
		}

		public void PersonalBroadcast(Player player, uint duration, string message)
		{
			var connection = (player.GetGameObject() as GameObject).GetComponent<NicknameSync>().connectionToClient;

			if (connection == null)
			{
				return;
			}

			cachedBroadcast.CallTargetAddElement(connection, message, duration, false);
		}

		public void ClearBroadcasts() => cachedBroadcast.CallRpcClearElements();

		public void Broadcast(uint duration, string message)
		{
			cachedBroadcast.CallRpcAddElement(message, duration, false);
		}
	}
}
