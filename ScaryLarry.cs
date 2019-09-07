using ArithFeather.CustomAPI;
using MEC;
using Smod2.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ArithFeather.ClassDEscape
{
	public class ScaryLarry
	{
		public readonly Player player;
		private readonly bool allowManualTeleport;
		private readonly int secondsUntil106Teleport;

		public ScaryLarry(Player player, bool allowManualTeleport, int secondsUntil106Teleport)
		{
			this.player = player;
			this.allowManualTeleport = allowManualTeleport;
			this.secondsUntil106Teleport = secondsUntil106Teleport;

			coro1 = Timing.RunCoroutine(_LarryTimer());
		}

		public bool AllowPortal = false;
		private bool autoTeleport = false;
		private int larryTpTimer;
		private CoroutineHandle coro1;
		private CoroutineHandle coro2;

		public bool AttemptTeleport()
		{
			if ((autoTeleport || allowManualTeleport) && !coro2.IsRunning)
			{
				coro2 = Timing.RunCoroutine(_TeleportLarry());
				return true;
			}
			else
			{
				return false;
			}
		}

		public void KillYourself()
		{
			coro1.IsRunning = false;
			coro2.IsRunning = false;
		}

		/// <summary>
		/// Makes sure a larry teleports every # seconds and creates a new portal after
		/// </summary>
		private IEnumerator<float> _LarryTimer()
		{
			player.Broadcast(8, $"You will automatically teleport every {secondsUntil106Teleport} seconds.");
			if (allowManualTeleport) player.Broadcast(4, $"You can trigger the teleport manually.");

			yield return Timing.WaitForSeconds(1);

			var playerScript = (player.GetGameObject() as GameObject).GetComponent<Scp106PlayerScript>();
			var moveSync = playerScript.GetComponent<FallDamage>();

			yield return Timing.WaitForSeconds(1);
			yield return Timing.WaitUntilFalse(() => playerScript.goingViaThePortal);

			AllowPortal = true;
			playerScript.CallCmdMakePortal();
			yield return Timing.WaitForSeconds(0.5f);
			AllowPortal = false;

			larryTpTimer = secondsUntil106Teleport - 3;

			while (true)
			{
				yield return Timing.WaitForSeconds(1);
				larryTpTimer -= 1;

				if (larryTpTimer <= 0)
				{
					yield return Timing.WaitUntilTrue(() => moveSync.isGrounded);

					autoTeleport = true;
					playerScript.CallCmdUsePortal();

					yield return Timing.WaitForSeconds(3f);
				}
				else if (larryTpTimer <= 10)
				{
					player.Broadcast(1, $"Teleport in {larryTpTimer}");
				}
			}
		}

		private IEnumerator<float> _TeleportLarry()
		{
			var script = (player.GetGameObject() as GameObject).GetComponent<Scp106PlayerScript>();

			player.Broadcast(2, $"Teleporting...");

			autoTeleport = false;
			larryTpTimer = secondsUntil106Teleport + 3;

			yield return Timing.WaitForSeconds(3);
			yield return Timing.WaitUntilFalse(() => script.goingViaThePortal);

			AllowPortal = true;
			script.CallCmdMakePortal();
			yield return Timing.WaitForSeconds(0.5f);
			AllowPortal = false;
		}

	}
}