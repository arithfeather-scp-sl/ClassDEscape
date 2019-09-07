using MEC;
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
	internal class LightControl
	{
		private Plugin plugin;

		public LightControl(Plugin plugin) => this.plugin = plugin;

		private Generator079 cachedGenerator;
		private Room[] cachedRooms;
		private CoroutineHandle keepLightsOffCoroutine;

		public void WaitingForPlayersEvent()
		{
			cachedGenerator = UnityEngine.Object.FindObjectOfType<Generator079>();
			cachedRooms = plugin.Server.Map.Get079InteractionRooms(Smod2.API.Scp079InteractionType.CAMERA);
		}

		public void RoundRestartEvent() => Timing.KillCoroutines(keepLightsOffCoroutine);

		public int FlickerTimer { get; set; }

		public void FlickerLights(ZoneType zone)
		{
			if (zone == ZoneType.UNDEFINED || zone == ZoneType.ENTRANCE) throw new Exception("Undefined and entrance zone can't have lights flickered");

			foreach (var room in cachedRooms)
			{
				if (room.ZoneType == zone)
				{
					room.FlickerLights();
				}
			}
		}

		public void HczLightsOn() => keepLightsOffCoroutine.IsRunning = false;

		public void HczLightsOff()
		{
			if (!keepLightsOffCoroutine.IsRunning)
			{
				FlickerTimer = 0;
				keepLightsOffCoroutine = Timing.RunCoroutine(_KeepLightsOff());
			}
		}

		private IEnumerator<float> _KeepLightsOff()
		{
			while (true)
			{
				if (FlickerTimer == 0)
				{
					FlickerTimer = 10;
					cachedGenerator.CallRpcOvercharge();
				}

				yield return Timing.WaitForSeconds(1);

				FlickerTimer--;
			}
		}
	}
}
