using MEC;
using Smod2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArithFeather.CustomAPI
{
	internal class CustomPlayerManager
	{
		private Plugin plugin;

		public CustomPlayerManager(Plugin plugin) => this.plugin = plugin;

		public event CustomAPI.PlayerDisconnect OnPlayerDisconnect;

		public void DisconnectEvent() => Timing.RunCoroutine(_DelayedDisconnect());

		private IEnumerator<float> _DelayedDisconnect()
		{
			var players = PlayerManager.singleton.players;
			var playerCount = players.Length;
			var oldPlayers = new List<int>();
			var newPlayers = new List<int>();

			for (int i = 0; i < playerCount; i++)
			{
				NicknameSync component = players[i].GetComponent<NicknameSync>();
				if (component != null)
				{
					oldPlayers.Add(component.playerControllerId);
				}
			}

			yield return Timing.WaitForOneFrame;

			for (int i = 0; i < playerCount; i++)
			{
				NicknameSync component = players[i].GetComponent<NicknameSync>();
				if (component != null)
				{
					newPlayers.Add(component.playerControllerId);
				}
			}

			var previousPlayerCount = oldPlayers.Count;
			var currentPlayerCount = newPlayers.Count;

			//Remove all matches to current players
			for (int i = currentPlayerCount; i >= 0; i--)
			{
				var ppID = oldPlayers[i];

				for (int j = 0; j < currentPlayerCount; j++)
				{
					if (ppID == newPlayers[j])
					{
						oldPlayers.RemoveAt(i);
						break;
					}
				}
			}

			if (oldPlayers.Count != 1)
			{
				plugin.Error("Could not find disconnected player");
			}
			else
			{
				OnPlayerDisconnect?.Invoke(oldPlayers[0]);
			}
		}
	}
}
