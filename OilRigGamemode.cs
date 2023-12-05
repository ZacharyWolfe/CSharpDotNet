using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
	[Info ("OilRigGamemode", "Blood", "0.0.1")]
	[Description ("Give them the damn gamemode")]
	public class OilRigGamemode : RustPlugin
	{
		private const string minicopterAssetPrefab = "assets/content/vehicles/minicopter/minicopter.entity.prefab";
		private const string crateAssetPrefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";

		struct Team
		{
			public List<BasePlayer> teamMembers;
			public List<BasePlayer> connectedMembers;
			public bool color;
			public string gamemodeQueuedFor;
			public BasePlayer identifier;
			public bool inGame;         
			public ulong matchID;
			public int roundsWon;
			public int points;
			public int crateHackWins;
			public bool canForfeit;
		};
		
		struct Match
		{
			public Team a;
			public Team b;
			public int onRound;
			public int rounds;
			public bool started;
			public bool ended;
			public List<BasePlayer> teamsCombined;
			public List<BasePlayer> deathsTeamA;
			public List<BasePlayer> deathsTeamB;
			public List<BasePlayer> spectators;
			public List<BasePlayer> PenalizePlayers;
			public List<PlayerHelicopter> minicopters;
			public ulong ID;
			public string winner;
			public float timeLeft;
			public HackableLockedCrate crateKek;
			public bool increaseRoundFlag;
			public bool roundOver;
		};

		List<Match> Matches = new List<Match>();                  
		Dictionary<Team, int> Teams = new Dictionary<Team, int>();
		Dictionary<BasePlayer, float> DisconnectedPlayersAlert = new Dictionary<BasePlayer, float>();
		List<Team> teamsToRemove = new List<Team> ();
		Timer displayMessageTimer = null;
		Timer newTimer = null;

		#region ChatCommands
		/*
		[ChatCommand ("forfeit")]
		private void ForfeitCMD(BasePlayer caller){
			if (IsInMatch(caller)){
				Match match = GetMatch(caller);

				if (match.a.canForfeit && match.a.teamMembers.Contains(caller)){
					End(match);
					match.a.canForfeit = false;
				}
				else if (match.b.canForfeit){
					End(match);
					match.a.canForfeit = false;
				}
				else{
					SendReply(caller, "You don't have permission to do this.");
				}
			}
			else{
				SendReply(caller, "You don't have permission to do this.");
			}
		}
		*/

		[ChatCommand ("qleave")]
		private void LeaveQueue(BasePlayer caller)
		{
			KeyValuePair<Team, int> removeTeam = new KeyValuePair<Team, int> ();
			bool foundTeamInQueue = false;
			if (caller == null)
				return;

			if (caller == caller.Team.GetLeader())
			{
				foreach (KeyValuePair<Team, int> entry in Teams)
				{
					if (entry.Key.identifier == caller)
					{
						removeTeam = entry;
						foundTeamInQueue = true;
						break;
					}
				}
				if (foundTeamInQueue)
				{
					Teams.Remove (removeTeam.Key);
					SendReply (caller, "Successfully removed your team from the queue.");
				}
				else
				{
					SendReply (caller, "Your team is not in the queue, try queuing first.");
				}
			}
			else
			{
				SendReply (caller, "Only the Leader of your team can cancel a queue.");
			}
		}

		[ChatCommand("rig")]
		private void CmdRig (BasePlayer initiator)
		{
			if (initiator == null) return;

			if (initiator.Team == null)// || initiator.currentTeam < 1) I honestly don't know how this was working before but it should be initiator.Team.members.Count < 1 but even then we allow solos
			{
				initiator.ChatMessage ("Not a large enough group for this event. Please ensure that you are on a team and you .");
				return;
			}
			
			if (initiator != initiator.Team.GetLeader())
			{
				SendReply (initiator, "Only the team leader is allowed to initiate queuing.");
				return;
			}

			foreach (KeyValuePair<Team, int> check in Teams)
			{
				if (check.Key.identifier == initiator) 
				{
					SendReply (initiator, "Your team is already in the Queue for " + check.Key.gamemodeQueuedFor + ".");
					return;
				}
			}

			if (IsInMatch (initiator))
			{
				SendReply(initiator, "You are already in a match, you cannot queue at this time.");
				return;
			}
			// Check if the person queuing is already in the queue

			else
			{
				//int teamcount = 0;
				Team newTeam = new Team
				{
					teamMembers = new List<BasePlayer> (),
					connectedMembers = new List<BasePlayer>(),
					identifier = initiator,
					gamemodeQueuedFor = "",
					inGame = false
				};
				foreach (ulong teamMemberSteamID64 in initiator.Team.members)
				{
					BasePlayer teammate = BasePlayer.FindByID (teamMemberSteamID64);
					if (teammate != null && teammate.IsConnected)
					{
						SendReply (initiator, "Member connected: " + teammate.displayName.ToString ());
						newTeam.teamMembers.Add (teammate);
						newTeam.connectedMembers.Add (teammate);
						//teamcount++;
					}
					else if (teammate != null && !teammate.IsConnected)
					{
						initiator.Team.RemovePlayer (teammate.userID);
					}
				}
				switch (newTeam.teamMembers.Count)
				{
					case 1:
						newTeam.gamemodeQueuedFor = "Solos.";
						break;

					case 2:
						newTeam.gamemodeQueuedFor = "Duos.";
						break;

					case 3:
						newTeam.gamemodeQueuedFor = "Trios.";
						break;

					case 4:
						newTeam.gamemodeQueuedFor = "Quads.";
						break;

					default:
						SendReply (initiator, "Team size invalid for this gamemode (Small Oil Rig). Err: 1 < Team size < 5.");
						return;
			}
			Teams.Add (newTeam, newTeam.teamMembers.Count);
			SendMessage (newTeam, "Now queuing for " + newTeam.gamemodeQueuedFor);

			if (newTimer == null){
				Server.Broadcast("Creating an instance of timer");
				newTimer = timer.Every (3f, () =>
				{
					if (Teams.Count > 1) // there are two teams in the queue
					{
						KeyValuePair<Team, int> team = Teams.First ();
						KeyValuePair<Team, int> entry = new KeyValuePair<Team, int> ();
						foreach (KeyValuePair<Team, int> check in Teams)
						{
							if (check.Key.teamMembers != team.Key.teamMembers && team.Key.teamMembers.Count == check.Key.teamMembers.Count)
							{
								teamsToRemove.Add (team.Key);
								teamsToRemove.Add (check.Key);
								if (newTimer != null)
								{
									newTimer.Destroy ();
									newTimer = null;
								}
								entry = check;
								break;
							}
						}
						StartGame (team, entry);
						foreach (Team remove in teamsToRemove)
						{
							Teams.Remove (remove);
						}
						teamsToRemove.Clear ();
					}
				});
				displayMessageTimer = timer.Every (20f, () =>
				{
					if (Teams.ContainsKey (newTeam))
					{
						SendReply (initiator, "You are in the Queue for " + newTeam.gamemodeQueuedFor); // + " at an elo of " + getElo(initiator));
					}
					else
					{
						displayMessageTimer.Destroy ();
						displayMessageTimer = null;
					}
				});
			}
		}
		#endregion

		private void StartGame (KeyValuePair<Team, int> teamA, KeyValuePair<Team, int> teamB)
		{
			bool selectedTeamColor 	= Random.Range (0, 2) == 1;
			bool selectedEntryColor = !selectedTeamColor;

			Team teamANew = new Team {
				color 				= selectedTeamColor,
				gamemodeQueuedFor  	= teamA.Key.gamemodeQueuedFor,
				matchID 			= teamA.Key.matchID,
				identifier 			= teamA.Key.identifier,
				inGame 				= teamA.Key.inGame,
				teamMembers 		= teamA.Key.teamMembers,
				connectedMembers 	= teamA.Key.connectedMembers,
				roundsWon 			= 0
			};

			Team teamBNew = new Team
			{
				color 				= selectedEntryColor,
				gamemodeQueuedFor 	= teamB.Key.gamemodeQueuedFor,
				matchID 			= teamB.Key.matchID,
				identifier			= teamB.Key.identifier,
				inGame 				= teamB.Key.inGame,
				teamMembers 		= teamB.Key.teamMembers,
				connectedMembers 	= teamB.Key.connectedMembers,
				roundsWon 			= 0
			};

			KeyValuePair<Team, int> teamAKVP = new KeyValuePair<Team, int> (teamANew, teamANew.teamMembers.Count);
			KeyValuePair<Team, int> teamBKVP = new KeyValuePair<Team, int> (teamBNew, teamBNew.teamMembers.Count);

			teamANew.inGame = true;
			teamBNew.inGame = true;

			if (teamBNew.color)
			{
				SendMessage (teamBNew, "You are on the <color=#316bf5>BLUE</color> team.");
				SendMessage (teamBNew, "You must wait 10 seconds before <color=#ed2323>DEFENDING</color> the OilRig from your opponent.");
			}
			else
			{
				SendMessage (teamBNew, "You are on the <color=#ed2323>RED</color> team.");
				SendMessage (teamBNew, "Get to the Oil Rig and successfully <color=#ed2323>ELIMINATE</color> all enemies before the 5 minute timer runs out.");
			}
			if (teamANew.color)
			{
				SendMessage (teamANew, "You are on the <color=#316bf5>BLUE</color> team.");
				SendMessage (teamANew, "You must wait 10 seconds before <color=#ed2323>DEFENDING</color> the OilRig from your opponent.");
			}
			else
			{
				SendMessage (teamANew, "You are on the <color=#ed2323>RED</color> team.");
				SendMessage (teamANew, "Get to the Oil Rig and successfully <color=#ed2323>ELIMINATE</color> all enemies before the 5 minute timer runs out.");
			}
			

			Match newMatch = new Match
			{
				a 				= teamANew,
				b 				= teamBNew,
				started 		= false,
				onRound 		= 1,
				rounds 			= 3,
				ended 			= false,
				ID 				= teamANew.identifier.userID + teamBNew.identifier.userID, // Unique key based on the leaders of both teams' game IDs
				deathsTeamA 	= new List<BasePlayer> (),
				deathsTeamB 	= new List<BasePlayer> (),
				spectators 		= new List<BasePlayer> (),
				teamsCombined 	= new List<BasePlayer> (),
				PenalizePlayers = new List<BasePlayer> (),
				minicopters 	= new List<PlayerHelicopter> (),
			};
			var objec 		= newMatch;
			objec.crateKek	= StartTimedCrate (newMatch);
			newMatch 		= objec;
			newMatch.teamsCombined = CombineTeams (newMatch.a, newMatch.b);

			SendToGame (teamAKVP, teamANew.color, newMatch);
			SendMessage (teamAKVP.Key, "You are facing team, " + teamBKVP.Key.identifier.displayName + ".");

			SendToGame (teamBKVP, teamBNew.color, newMatch);
			SendMessage (teamBKVP.Key, "You are facing team, " + teamAKVP.Key.identifier.displayName + ".");

			Matches.Add (newMatch);
			CheckGame (newMatch);
		}

		/*
		private void RoundIncrease (Match match)
		{
			Server.Broadcast ("In round increase");
			if (match.ID == 0)
			{
				return;
			}
			Team teamANew = new Team
			{
				color = match.a.color,
				gamemodeQueuedFor = match.a.gamemodeQueuedFor,
				matchID = match.a.matchID,
				identifier = match.a.identifier,
				inGame = match.a.inGame,
				teamMembers = match.a.teamMembers,
				connectedMembers = match.a.connectedMembers,
				roundsWon = match.a.roundsWon
			};

			if (match.a.color) // if the A team is blue
			{
				Server.Broadcast ("Increasing roundwon of team blue " + teamANew.roundsWon);
				var objec = teamANew;
				objec.roundsWon++;
				teamANew = objec;
				Server.Broadcast ("after increasing roundwon of team blue " + teamANew.roundsWon);
			}

			Team teamBNew = new Team
			{
				color = match.b.color,
				gamemodeQueuedFor = match.b.gamemodeQueuedFor,
				matchID = match.b.matchID,
				identifier = match.b.identifier,
				inGame = match.b.inGame,
				teamMembers = match.b.teamMembers,
				connectedMembers = match.b.connectedMembers,
				roundsWon = match.b.roundsWon
			};

			if (match.b.color)
			{
				Server.Broadcast ("Increasing roundwon of team blue " + teamBNew.roundsWon);
				var objec = teamBNew;
				objec.roundsWon++;
				teamBNew = objec;
				Server.Broadcast ("after increasing roundwon of team blue " + teamBNew.roundsWon);
			}

			//KeyValuePair<Team, int> teamAKVP = new KeyValuePair<Team, int> (teamANew, teamANew.teamMembers.Count);
			//KeyValuePair<Team, int> teamBKVP = new KeyValuePair<Team, int> (teamBNew, teamBNew.teamMembers.Count);

			Match match2 = new Match {
				a = teamANew,
				b = teamBNew,
				deathsTeamA = match.deathsTeamA,
				deathsTeamB = match.deathsTeamB,
				crateKek = match.crateKek,
				teamsCombined = match.teamsCombined,
				ended = match.ended,
				ID = match.ID,
				minicopters = match.minicopters,
				onRound = match.onRound,
				PenalizePlayers = match.PenalizePlayers,
				rounds = match.rounds,
				spectators = match.spectators,
				started = match.started,
				timeLeft = match.timeLeft,
				winner = match.winner,
				increaseRoundFlag = true
			};

			if (teamANew.color)
			{
				SendMessage (teamANew, "Your team successfully won the round by letting the crate time expire. ");
				SendMessage (teamBNew, "Your team did not successfully eliminate all players before the time expired. ");
			}
			else
			{
				SendMessage (teamBNew, "Your team successfully won the round by letting the crate time expire. ");
				SendMessage (teamANew, "Your team did not successfully eliminate all players before the time expired. ");
			}

			Server.Broadcast ("Checking match in roundincrease with flag of " + match2.increaseRoundFlag);
			CheckGame (match2);
		}
		*/

		private void SendToGame (KeyValuePair<Team, int> team, bool color, Match match)
		{
			Vector3 [] rigSpawns = new Vector3[4];
			Vector3 outsideSpawn;

			rigSpawns [0].x = -323.398f;
			rigSpawns [0].y = 22.4263f;
			rigSpawns [0].z = -364.819f;

			rigSpawns [1].x = -334.93f;
			rigSpawns [1].y = 22.72f;
			rigSpawns [1].z = -363.82f;

			rigSpawns [2].x = -326.21f;
			rigSpawns [2].y = 17.93f;
			rigSpawns [2].z = -360.49f;

			rigSpawns [3].x = -348.74f;
			rigSpawns [3].y = 30.79f;
			rigSpawns [3].z = -368.81f;

			outsideSpawn.x 	= -426.597f;
			outsideSpawn.y 	= 20.0818f;
			outsideSpawn.z 	= -238.781f;

			int [] itemIDsWear = { -194953424, 1110385766, 1850456855, -1549739227, 1366282552 };
			int i = 0;
			foreach (BasePlayer teamMember in team.Key.teamMembers)
			{
				if (i % 3 == 0){
					i = 0;
				}
				if (teamMember == null)
					continue;
				PlayerInventory Inventory = teamMember.inventory;

				if (teamMember.isMounted)
				{
					teamMember.EnsureDismounted ();
				}
				if (!Inventory.containerWear.IsLocked ())
				{
					Inventory.containerWear.SetLocked (true);
				}

				Inventory.Strip ();
				if (color)
				{
					Teleport (teamMember, rigSpawns[i]);
					timer.Repeat (.125f, 80, () =>
					{
						bool move = (teamMember.transform.position.x >= rigSpawns [i].x + 3.0f 	|| 
									 teamMember.transform.position.z >= rigSpawns [i].z + 3.0f) || 

									(teamMember.transform.position.x <= rigSpawns [i].x - 3.0f 	|| 
									 teamMember.transform.position.z <= rigSpawns [i].z - 3.0f) || 
									
									(teamMember.transform.position.x >= rigSpawns [i].x + 3.0f 	|| 
									 teamMember.transform.position.z <= rigSpawns [i].z - 3.0f) || 
									
									(teamMember.transform.position.x <= rigSpawns [i].x - 3.0f 	|| 
									 teamMember.transform.position.z >= rigSpawns [i].z + 3.0f);

						if (teamMember != null && move)
							teamMember.MovePosition (rigSpawns [i]);
					});
				}
				else
				{
					Vector3 newSpawn;
					newSpawn.x = outsideSpawn.x;
					newSpawn.y = outsideSpawn.y + 25.0f;
					newSpawn.z = outsideSpawn.z;

					Teleport (teamMember, newSpawn);
					teamMember.UpdateNetworkGroup ();
					teamMember.UpdateSurroundings ();
				}

				GiveKit (teamMember, color);
				Metabolize (teamMember);
				i++;
			}
			if (color == false){
				OnEntitySpawned (team.Key, match);
			}
		}

		private HackableLockedCrate StartTimedCrate (Match match){
			Vector3 cratePos;
			cratePos.x = -322.63f;
			cratePos.y = 27.18f;
			cratePos.z = -342.04f;

			const float REQUIREDHACKSECONDS = 300f;
			HackableLockedCrate lockedCrate;
			lockedCrate = (HackableLockedCrate) GameManager.server.CreateEntity (crateAssetPrefab, cratePos, match.a.identifier.ServerRotation);
			
			if (lockedCrate != null)
			{
				lockedCrate.SpawnAsMapEntity ();
				lockedCrate.StartHacking ();
				lockedCrate.shouldDecay = false;
				lockedCrate.enableSaving = false;
				lockedCrate.hackSeconds = HackableLockedCrate.requiredHackSeconds - REQUIREDHACKSECONDS;
				return lockedCrate;
			}
			return lockedCrate;
		}
		private void CheckGame(Match match)
		{
			Timer myTimer = null;
			myTimer = timer.Every (1f, () =>
			{
				foreach (BasePlayer teammate in match.a.teamMembers)
				{
					if (teammate != null && teammate.IsConnected && !match.a.connectedMembers.Contains(teammate)){
						match.a.connectedMembers.Add(teammate);
					}	
					else if (teammate != null && !teammate.IsConnected){
						SwapLeadership(teammate);
						//Kick(teammate);
					}
				}
				foreach (BasePlayer teammate2 in match.b.teamMembers)
				{
					if (teammate2 != null && teammate2.IsConnected && !match.b.connectedMembers.Contains(teammate2)){
						match.b.connectedMembers.Add (teammate2);
					}
					else if (teammate2 != null && !teammate2.IsConnected){
						SwapLeadership(teammate2);
						//Kick(teammate2);
					}	
				}

			//match.a.identifier.Team.
				if (match.increaseRoundFlag && match.rounds == match.onRound)
				{
					match.roundOver = true;
					End (match);
					match.increaseRoundFlag = false;
				}
				else if ((match.increaseRoundFlag || match.timeLeft == 300f) && match.rounds < match.onRound)
				{
					if (match.b.color){
						Team teamB = new Team
						{
							color = match.b.color,
							matchID = match.b.matchID,
							gamemodeQueuedFor = match.b.gamemodeQueuedFor,
							identifier = match.b.identifier,
							inGame = match.b.inGame,
							teamMembers = match.b.teamMembers,
							connectedMembers = match.b.connectedMembers,
							roundsWon = match.b.roundsWon + 1
						};
						SendMessage (teamB, "<color=#316bf5>BLUE</color> won the round by successully expiring the crate time.");
						SendMessage (match.a, "<color=#316bf5>BLUE</color> won the round  by successully expiring the crate time.");
						match.b = teamB;
					}
					else{
						Team teamA = new Team
						{
							color = match.a.color,
							matchID = match.a.matchID,
							gamemodeQueuedFor = match.a.gamemodeQueuedFor,
							identifier = match.a.identifier,
							inGame = match.a.inGame,
							teamMembers = match.a.teamMembers,
							connectedMembers = match.a.connectedMembers,
							roundsWon = match.a.roundsWon + 1
						};
						SendMessage (teamA, "<color=#316bf5>BLUE</color> won the round by successully expiring the crate time.");
						SendMessage (match.b, "<color=#316bf5>BLUE</color> won the round  by successully expiring the crate time.");
						match.b = teamA;
					}
					match.roundOver = true;
					match.onRound++;
					Reset (match);
					match.increaseRoundFlag = false;
					match.ended = false;
				}
				else
				{
					//match.timeLeft++;
					if (match.rounds == match.onRound && (match.deathsTeamA.Count >= match.a.teamMembers.Count || match.deathsTeamB.Count >= match.b.teamMembers.Count))
					{
						if (match.a.roundsWon > match.b.roundsWon)
						{
							match.winner = match.a.identifier.displayName.ToString ();
						}
						else
						{
							match.winner = match.b.identifier.displayName.ToString ();
						}

						End (match);
						if (myTimer != null)
						{
							myTimer.Destroy ();
							myTimer = null;
						}
					}
					else if (match.deathsTeamB.Count >= match.b.teamMembers.Count || match.deathsTeamA.Count >= match.a.teamMembers.Count)
					{
						if (match.deathsTeamA.Count >= match.a.teamMembers.Count)
						{
							Team teamB = new Team
							{
								color = match.b.color,
								matchID = match.b.matchID,
								gamemodeQueuedFor = match.b.gamemodeQueuedFor,
								identifier = match.b.identifier,
								inGame = match.b.inGame,
								teamMembers = match.b.teamMembers,
								connectedMembers = match.b.connectedMembers,
								roundsWon = match.b.roundsWon
							};
							if (!match.started && match.b.color)
							{
								SendMessage (teamB, "You are on the <color=#316bf5>BLUE</color> team.");
								SendMessage (teamB, "You must wait 10 seconds before <color=#ed2323>DEFENDING</color> the OilRig from your opponent.");
							}
							else if (!match.started && !match.b.color)
							{
								SendMessage (teamB, "You are on the <color=#ed2323>RED</color> team.");
								SendMessage (teamB, "Get to the Oil Rig and successfully <color=#ed2323>ELIMINATE</color> all enemies before the 5 minute timer runs out.");
							}

							if (match.b.color)
							{
								SendMessage (teamB, "<color=#316bf5>BLUE</color> won the round.");
								SendMessage (match.a, "<color=#316bf5>BLUE</color> won the round.");
							}
							else
							{
								SendMessage (teamB, "<color=#ed2323>RED</color> won the round.");
								SendMessage (match.a, "<color=#ed2323>RED</color> won the round.");
							}
							teamB.roundsWon++;
							match.roundOver = true;
							match.b = teamB;
						}
						else
						{
							Team teamA = new Team
							{
								color = match.a.color,
								matchID = match.a.matchID,
								gamemodeQueuedFor = match.a.gamemodeQueuedFor,
								identifier = match.a.identifier,
								inGame = match.a.inGame,
								teamMembers = match.a.teamMembers,
								connectedMembers = match.a.connectedMembers,
							};
							if (!match.started && !teamA.color)
							{
								SendMessage (teamA, "You are on the <color=#ed2323>RED</color> team.");
								SendMessage (teamA, "Get to the Oil Rig and successfully <color=#ed2323>ELIMINATE</color> all enemies before the 5 minute timer runs out.");
							}
							else if (!match.started && teamA.color)
							{
								SendMessage (teamA, "You are on the <color=#316bf5>BLUE</color> team.");
								SendMessage (teamA, "You must wait 10 seconds before <color=#ed2323>DEFENDING</color> the OilRig from your opponent.");
							}

							if (match.a.color)
							{
								SendMessage (match.b, "<color=#316bf5>BLUE</color> won the round.");
								SendMessage (teamA, "<color=#316bf5>BLUE</color> won the round.");
							}
							else
							{
								SendMessage (match.b, "<color=#ed2323>RED</color> won the round.");
								SendMessage (teamA, "<color=#ed2323>RED</color> won the round.");
							}
							teamA.roundsWon++;
							match.roundOver = true;
							match.a = teamA;
						}
						match.onRound++;
						Reset (match);
						match.ended = false;
					}
					match.ended = false;
					//if (onlineConnectionsTeamA.Count != match.a.identifier.Team.members.Count)
					//{
					//passengers = teamList.Except (drivers).ToList ();
					//	removePlayersTeamA = match.a.identifier.Team.members.Except (onlineConnectionsTeamA.Values).ToList ();
					//}
					//if (onlineConnectionsTeamB.Count != match.b.identifier.Team.members.Count)
					//{
					//passengers = teamList.Except (drivers).ToList ();
					//	removePlayersTeamB = match.b.identifier.Team.members.Except (onlineConnectionsTeamB.Values).ToList ();
					//}
				}
			});
		}

		private void Reset (Match match)
		{
			if (match.ID == 0)
			{
				Server.Broadcast ("reset matchid == 0");
				return;
			}

			var obj = match;
			//match.crateKek = StartTimedCrate (match);
			obj.increaseRoundFlag = false;
			

			//AwardPoints (match);
			List<PlayerHelicopter> MinicoptersToRemove = new List<PlayerHelicopter> ();
			match.increaseRoundFlag = false;
			match.deathsTeamB.Clear ();
			match.deathsTeamA.Clear ();
			match.spectators.Clear ();

			if (match.crateKek != null)
			{
				match.crateKek.Kill ();
			}

			//obj.crateKek = StartTimedCrate (match);
			match = obj;

			if (match.minicopters != null)
			{
				foreach (PlayerHelicopter mini in match.minicopters)
				{
					if (mini != null && !mini.IsDestroyed)
					{
						mini.Kill ();
						if (match.minicopters != null) MinicoptersToRemove.Add (mini);
					}
				}
				foreach (PlayerHelicopter minicopterfuckingdie in MinicoptersToRemove)
				{
					match.minicopters.Remove (minicopterfuckingdie);
				}
			}
			if (!match.ended)
			{
				Respawn (match);
			}
		}

		private void Respawn (Match match)
		{
			KeyValuePair<Team, int> teamA = new KeyValuePair<Team, int>(match.a, match.a.teamMembers.Count);
			KeyValuePair<Team, int> teamB = new KeyValuePair<Team, int> (match.b, match.b.teamMembers.Count);
			match.roundOver = false;
			SendToGame (teamA, match.a.color, match);
			SendToGame (teamB, match.b.color, match);
		}


		#region Helper Functions
		private List<BasePlayer> CombineTeams (Team a, Team b)
		{
			List<BasePlayer> players = new List<BasePlayer> ();
			foreach (BasePlayer player in a.teamMembers)
			{
				if (player != null)
					players.Add (player);
			}
			foreach (BasePlayer player2 in b.teamMembers)
			{
				if (player2 != null)
					players.Add (player2);
			}
			return players;
		}

		public void GiveKit (BasePlayer player, bool teamColor)
		{

			PlayerInventory Inventory = player.inventory;

			Inventory.Strip ();
			Item item = ItemManager.CreateByItemID (1545779598, 1);
			var weapon = item.GetHeldEntity () as BaseProjectile;
			if (weapon != null)
			{
				weapon.primaryMagazine.contents = 0; // unload the old ammo
				Item holosight = ItemManager.CreateByItemID (442289265, 1);
				holosight.MoveToContainer (item.contents);

				// Add Laser to the assault rifle
				Item laser = ItemManager.CreateByItemID (-132516482, 1);
				laser.MoveToContainer (item.contents);
				weapon.SendNetworkUpdateImmediate(false); // update
				weapon.primaryMagazine.contents = weapon.primaryMagazine.capacity; // load new ammo
			}
			GiveItems (Inventory, 1, Inventory.containerWear);                                               // ARMOR
			if (teamColor)
			{
				Inventory.GiveItem (ItemManager.CreateByItemID (1751045826, 1, 14178), Inventory.containerWear);				// HOODIE
				Inventory.GiveItem (ItemManager.CreateByItemID (237239288, 1, 10001), Inventory.containerWear);					// PANTS
			}
			else
			{
				Inventory.GiveItem (ItemManager.CreateByItemID (1751045826, 1), Inventory.containerWear);                       // HOODIE
				Inventory.GiveItem (ItemManager.CreateByItemID (237239288, 1, 3099244148), Inventory.containerWear);			// PANTS
			}
			Inventory.GiveItem (item);                                                                                          // ASSAULT RIFLE
			Inventory.GiveItem (ItemManager.CreateByItemID (-1211166256, 128), Inventory.containerMain);						// AMMO
			Inventory.GiveItem (ItemManager.CreateByItemID (1079279582, 2), Inventory.containerBelt);                           // SYRINGE
			Inventory.GiveItem (ItemManager.CreateByItemID (1079279582, 2), Inventory.containerBelt);                           // SYRINGE
			Inventory.GiveItem (ItemManager.CreateByItemID (1079279582, 1), Inventory.containerBelt);                           // SYRINGE
			Inventory.GiveItem (ItemManager.CreateByItemID (-2072273936, 3), Inventory.containerBelt);                          // BANDAGE
		}

		private void SendMessage (Team team, string message)
		{
			foreach (BasePlayer player in team.teamMembers)
			{
				if (player != null)
					SendReply (player, message);
			}
		}

		private void GiveItems (PlayerInventory teamMemberInventory, int amount, ItemContainer whereOnPlayer)
		{
			int [] itemIDsWear = { -194953424, 1110385766, 1850456855, -1549739227, 1366282552 };
			foreach (int itemNumber in itemIDsWear)
			{
				teamMemberInventory.GiveItem (ItemManager.CreateByItemID (itemNumber, amount), whereOnPlayer);
			}
		}

		private void PlayCodeLock (Vector3 position)
		{
			Effect.server.Run ("assets/prefabs/locks/keypad/effects/lock.code.lock.prefab", position);
		}

		private void PlayCodeUnlock (Vector3 position)
		{
			Effect.server.Run ("assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab", position);
		}

		private void PlayLockedCrate (Vector3 position)
		{
			Effect.server.Run ("assets/prefabs/io/electric/other/alarmsound.prefab", position);
		}

		public void Metabolize (BasePlayer player)
		{
			if (player == null)
				return;

			player.health = 100f;
			player.metabolism.temperature.min = 32f;
			player.metabolism.temperature.max = 32f;
			player.metabolism.temperature.value = 32f;
			player.metabolism.poison.value = 0f;
			player.metabolism.calories.value = player.metabolism.calories.max;
			player.metabolism.hydration.value = player.metabolism.hydration.max;
			player.metabolism.wetness.max = 0f;
			player.metabolism.wetness.value = 0f;
			player.metabolism.radiation_level.max = 0f;
			player.metabolism.radiation_poison.max = 0f;

			player.metabolism.SendChangesToClient ();
		}

		public void DeMetabolize (BasePlayer player)
		{
			if (player == null)
				return;

			player.health = 53f;
			player.metabolism.calories.SetValue(81);
			player.metabolism.hydration.Reset();
			player.metabolism.SendChangesToClient ();
		}

		public void Teleport (BasePlayer player, Vector3 destination)
		{
			if (player == null || destination == Vector3.zero)
			{
				return;
			}

			player.Invoke (player.EndLooting, 0.01f);

			if (player.IsWounded ())
			{
				player.StopWounded ();
			}
			player.metabolism.bleeding.Set (0);

			if (player.IsSleeping ())
			{
				player.EndSleeping ();
			}

			if (player.IsDead ())
			{
				player.Respawn ();
			}

			Metabolize (player);

			player.Teleport (destination);

			if (player.IsConnected && (Vector3.Distance (player.transform.position, destination) > 50f))
			{
				player.SetPlayerFlag (BasePlayer.PlayerFlags.ReceivingSnapshot, true);
				player.ClientRPCPlayer (null, player, "StartLoading");
				player.UpdateNetworkGroup ();
				player.SendEntityUpdate ();
			}

			/*
			if (player == null || destination == Vector3.zero)
			{
				return;
			}

			player.Invoke (player.EndLooting, 0.01f);

			if (player.IsWounded ())
			{
				player.StopWounded ();
			}

			if (player.IsSleeping ())
			{
				player.EndSleeping ();
			}
			
			//player.Respawn ();
			Metabolize (player);

			player.Teleport (destination);

			if (player.IsConnected && (Vector3.Distance (player.transform.position, destination) > 50f))
			{
				player.SetPlayerFlag (BasePlayer.PlayerFlags.ReceivingSnapshot, true);
				player.ClientRPCPlayer (null, player, "StartLoading");
				player.UpdateNetworkGroup ();
				player.SendEntityUpdate ();
			}
			*/
		}
		void OnEntitySpawned (Team team, Match match)
		{
			List<BasePlayer> teamList 	= new List<BasePlayer> ();
			List<BasePlayer> drivers 	= new List<BasePlayer> ();
			List<BasePlayer> passengers = new List<BasePlayer> ();
			List<int> randIntList 		= new List<int> ();

			int miniCount = 0;

			if (team.teamMembers.Count % 2 == 0)
			{
				miniCount = team.teamMembers.Count / 2;
			}
			else
			{
				miniCount = (team.teamMembers.Count / 2) + 1;
			}
			Vector3 [] newPositions = new Vector3 [miniCount];
			for (float i = 0; i < miniCount; i += 1)
			{
				Vector3 newPos;
				newPos.x = (team.identifier.transform.position.x + (3.5f * i));
				newPos.y = team.identifier.transform.position.y;
				newPos.z = team.identifier.transform.position.z;
				newPositions [((int) i)] = newPos;
			}
			foreach (BasePlayer player in team.teamMembers)
			{
				if (player.IsConnected && player != null)
					teamList.Add (player);
			}
			for (int i = 0; i < teamList.Count; i++)
			{
				int randInt = Random.Range (0, teamList.Count);
				randIntList.Add (randInt);
				drivers.Add (teamList [randIntList [i]]);
			}
			passengers = teamList.Except (drivers).ToList ();

			for (int i = 0; i < miniCount; i++)
			{
				PlayerHelicopter mini = team.identifier.GetComponentInParent<PlayerHelicopter> ();

				mini = GameManager.server.CreateEntity (minicopterAssetPrefab, newPositions [i], team.identifier.ServerRotation) as PlayerHelicopter;
				if (mini == null)
					return;

				mini.OwnerID = drivers [i].userID;
				mini.startHealth = 750f;
				mini.Spawn ();

				if (mini == null || mini.IsDestroyed)
				{
					return;
				}

				BasePlayer owner = drivers [i];

				if (owner == null || !owner.CanInteract ())
				{
					return;
				}
				foreach (var mountPoint in mini.mountPoints)
				{
					if (mountPoint.isDriver)
					{
						if (owner != null)
						{
							mountPoint.mountable.MountPlayer (drivers [i]);
							
						}
					}
					else if (passengers.Count > 0)
					{
						mountPoint.mountable.MountPlayer(passengers[i]);
					}
				}
				ModifyFuel(mini);
				mini.engineController.FinishStartingEngine();
				match.minicopters.Add(mini);
				owner.SendEntityUpdate ();	
			}
			if (match.crateKek == null)
			{
				Match newMatch = new Match {
					a = match.a,
					b = match.b,
					deathsTeamA = match.deathsTeamA,
					deathsTeamB = match.deathsTeamB,
					crateKek = StartTimedCrate(match),
					teamsCombined = match.teamsCombined,
					ended = match.ended,
					ID = match.ID,
					minicopters = match.minicopters,
					onRound = match.onRound,
					PenalizePlayers = match.PenalizePlayers,
					rounds = match.rounds,
					spectators = match.spectators,
					started = match.started,
					timeLeft = match.timeLeft,
					winner = match.winner,
					increaseRoundFlag = match.increaseRoundFlag
				};
				match = newMatch;

			}
		}
		private void ModifyFuel(BaseEntity entity)
		{
			StorageContainer container = null;
			BaseVehicle baseVehicle = entity as BaseVehicle;
			if(baseVehicle != null)
			{
				container = baseVehicle.GetFuelSystem()?.fuelStorageInstance.Get(true);
			}
			HotAirBalloon baseBalloon = entity as HotAirBalloon;
			if(baseBalloon != null)
			{
				EntityFuelSystem fuelSystem = baseBalloon.fuelSystem;
				container = fuelSystem?.fuelStorageInstance.Get(true);
			}

			if(container == null)
			{
				return;
			}

			var item = container.inventory.GetSlot (0);
			if (item == null)
			{
				item = ItemManager.CreateByItemID (-946369541, 100); // The amount of lowgrade to add to the vehicle
				if (item == null)
				{
					return;
				}
				item.MoveToContainer (container.inventory);

				container.dropsLoot = false;
				container.inventory.SetFlag (ItemContainer.Flag.IsLocked, true);
				container.SetFlag (BaseEntity.Flags.Locked, true);
			}
		}
		private void GiveElo(Team winner, Team loser){
			foreach (BasePlayer winTeammate in winner.teamMembers){
				//Award(teammate);
			}
			foreach (BasePlayer loseTeammate in loser.teamMembers){
				//Penalize(teammate, false);
			}
		}
		
		private void OnPlayerDisconnected(BasePlayer player, string reason){
			if (IsInMatch (player))
			{
				Match match = GetMatch (player);

				if (match.a.teamMembers.Contains (player))
				{
					if (match.a.teamMembers.Count < 1 || match.a.teamMembers == null || (match.rounds == match.onRound && match.ended))
					{
						End (match);
					}
					else if (match.a.teamMembers.Count > 1){
						match.PenalizePlayers.Add(player);
						match.a.connectedMembers.Remove(player);
						match.a.teamMembers.Remove(Player);
						match.a.canForfeit = true;
						(match.a, "Your team is available to /forfeit, " + player.displayName.ToString() + " has disconnected from the match and has been penalized for each member of your team.");
					}
				}
				else
				{
					if (match.b.teamMembers.Count < 1 || match.b.teamMembers == null || (match.rounds == match.onRound))
					{
						End (match);
					}
					else if (match.b.teamMembers.Count > 1){
						match.PenalizePlayers.Add(player);
						match.b.connectedMembers.Remove(player);
						match.b.teamMembers.Remove(player);
						match.b.canForfeit = true;
						SendMessage(match.b, "Your team is available to /forfeit, " + player.displayName.ToString() + " has disconnected from the match and has been penalized for each member of your team.");
					}
				}
			}
			SwapLeadership(player);
			//Kick(player);
		}

		private void SwapLeadership(BasePlayer player){
			// swap leadership if player is teamleader
			if (player.Team != null && player == player.Team.GetLeader ()){
				int rand = Random.Range (0, player.Team.members.Count);
				ulong leaderID = player.Team.members [rand];
				BasePlayer leader = BasePlayer.FindByID (leaderID);
				player.Team.SetTeamLeader (leader.userID);
				leader.Team.RemovePlayer (player.userID);
			}
			else if (player.Team != null && player.Team.members.Count > 1 && player != player.Team.GetLeader()){
				BasePlayer leader = player.Team.GetLeader ();
				leader.Team.RemovePlayer (player.userID);
			}
		}
		/*
		private void Kick(BasePlayer player){
			
			else if (player == player.Team.GetLeader() && player.Team.members.count > 1){
				SwapLeadership(player);
			}
		}
		*/
		private void OnEntityDeath(BaseEntity entity, HitInfo hitInfo){
			if (entity == null)
				return;

			var victim = entity as BasePlayer;

			if (victim == null){
				return;
			}
			if (IsInMatch (victim))
			{
				Match match = GetMatch (victim);
				if (match.ID == 0)
					return;
				if (match.spectators.Contains (victim))
					return;
				if (match.a.teamMembers.Contains (victim) && !match.deathsTeamA.Contains (victim))
				{
					match.deathsTeamA.Add (victim);
				}
				else if (!match.deathsTeamB.Contains (victim))
				{
					match.deathsTeamB.Add (victim);
				}
				victim.inventory.Strip ();
				match.spectators.Add (victim);
				//if (match.deathsTeamA.Count == match )
				SendSpectate (victim);
			}
			else
			{
				SendHome(victim);
			}
		}
		private void End(Match match){
			if (match.a.roundsWon == 3)
			{
				Server.Broadcast (match.b.identifier.displayName.ToString () + " got their shit rocked, 3-0.");
			}
			else if (match.b.roundsWon == 3)
			{
				Server.Broadcast (match.a.identifier.displayName.ToString () + " got their shit rocked, 3-0.");
			}
			else
			{
				SendMessage (match.a, match.winner + " wins. " + match.a.roundsWon + "-" + match.b.roundsWon + ".");
				SendMessage (match.b, match.winner + " wins. " + match.b.roundsWon + "-" + match.a.roundsWon + ".");
				Matches.Remove (match);
				foreach (BasePlayer player in match.teamsCombined)
				{
					SendHome (player);
				}

				foreach (PlayerHelicopter mini in match.minicopters)
				{
					if (mini != null || !mini.IsDestroyed)
					{
						mini.Kill ();
					}
				}

				match.minicopters.Clear ();
				match.spectators.Clear ();
				match.deathsTeamA.Clear ();
				match.deathsTeamB.Clear ();
				match.ended 	= true;
				match.started 	= false;
				match.ID 		= 0;
				match.onRound 	= 0;
				match.rounds 	= 0;
				match.winner 	= null;
				if (match.crateKek != null)
				{
					match.crateKek.Kill ();
					match.crateKek = null;
				}

				if (match.PenalizePlayers.Count > 0)
				{
					foreach (BasePlayer rat in match.PenalizePlayers)
					{
						Penalize (rat, true, );
					}
				}

				match.a.teamMembers.Clear ();
				match.b.teamMembers.Clear ();
				if (Teams.ContainsKey (match.a))
				{
					Teams.Remove (match.a);
				}
				if (Teams.ContainsKey (match.b))
				{
					Teams.Remove (match.b);
				}
			}
		}
		private void SendHome(BasePlayer player){
			Vector3 spawn;
			spawn.x = -140.41f;
			spawn.y = 20.21f;
			spawn.z = -321.54f;
			player.inventory.Strip ();
			player.inventory.containerWear.SetLocked (false);
			player.Respawn ();
			Teleport(player, spawn);
			DeMetabolize (player);
		}

		private void SendSpectate (BasePlayer player){
			Vector3 deathLobby;
			deathLobby.x = -119.57f;
			deathLobby.y = 15.11f;
			deathLobby.z = 103.78f;

			/*
			deathLobby.x = -417.66f;
			deathLobby.y = 20.08f;
			deathLobby.z = -226.21f;
			*/
			Teleport (player, deathLobby);
		}
		private bool IsInMatch(BasePlayer player){
			foreach (Match match in Matches){
				foreach (BasePlayer entry in match.teamsCombined){
					if (player != null && entry == player && match.ID != 0){
						return true;
					}
				}
			}
			return false;
		}

		private void Penalize (BasePlayer player, bool leaver, int multiplier)
		{
			return;
			DisconnectedPlayersAlert.Add(player, eloLost);
			if (multiplier != 0){

			}
			else{

			}
		}

		private Match GetMatch(BasePlayer player){
			Match matchNotFound = new Match();
			foreach (Match match in Matches){
				foreach (BasePlayer entry in match.teamsCombined){
					if (entry == player){
						return match;
					}
				}
				MatchSet(matchNotFound, match);
			}
			matchNotFound.ID = 0;
			return matchNotFound;
		}

		private Match GetMatch (HackableLockedCrate crate)
		{
			Match matchNotFound = new Match ();
			foreach (Match match in Matches)
			{
				if (match.crateKek == crate)
				{
					return match;
				}
			}
			matchNotFound.ID = 0;
			return matchNotFound;
		}

		private void MatchSet(Match receiver, Match setter){
			receiver.a = setter.a;
			receiver.b = setter.b;
			receiver.started = setter.started;
			receiver.onRound = setter.onRound;
			receiver.rounds = setter.rounds;
			receiver.ended = setter.ended;
			receiver.ID = setter.ID; // Unique key based on the leaders of both teams' game IDs
			receiver.deathsTeamA = setter.deathsTeamA;
			receiver.deathsTeamB = setter.deathsTeamB;
			receiver.spectators = setter.spectators;
			receiver.teamsCombined = setter.teamsCombined;
		}

		object OnTeamKick (RelationshipManager.PlayerTeam playerTeam, BasePlayer player, ulong target)
		{
			if (IsInMatch (player))
			{
				return false;
			}
			else
			{
				return null;
			}
		}

		object OnTeamLeave (RelationshipManager.PlayerTeam playerTeam, BasePlayer player)
		{
			if (IsInMatch (player))
			{
				return false;
			}
			else
			{
				return null;
			}
		}

		/*
		void OnCrateHackEnd (HackableLockedCrate self)
		{
			Server.Broadcast ("oncratehackend crate == null " + (match.crateKek == null).ToString ());

			if (!match.roundOver && self != null)
			{
				SendMessage (match.b, "<color=#316bf5>BLUE</color> won the round by successully expiring the crate time.");
				SendMessage (match.a, "<color=#316bf5>BLUE</color> won the round by successully expiring the crate time.");
				self.Kill ();
				match.increaseRoundFlag = true;
				//RoundIncrease (match);
			}
		}
		*/
		
		private void OnPlayerConnected (BasePlayer player)
		{
			if (!player.IsValid())
			{
				return;
			}
			SendHome (player);

			if (player.Team == null)
			{
				RelationshipManager.PlayerTeam PlayerTeam = RelationshipManager.ServerInstance.CreateTeam ();
				PlayerTeam.teamLeader = player.userID;
				PlayerTeam.AddPlayer (player);
			}
			
			foreach (KeyValuePair<BasePlayer, float> disconnect in DisconnectedPlayersAlert){
				if (player == disconnect.Key)
					SendReply(disconnect.Key.displayName.ToString(), "Nice disconnect during the last match you played; you were penalized an extra " + disconnect.Value + " elo for leaving.");
			}
		}

		private void AwardPoints (Match match)
		{
			return;

			int miniCount = 0;

			List<PlayerHelicopter> minicoptersDestroyed = new List<PlayerHelicopter> ();


			if (match.b.color)
			{
				match.b.points += match.b.crateHackWins * 15;
			}
			else
			{
				if (match.b.teamMembers.Count % 2 == 0)
				{
					miniCount = match.b.teamMembers.Count / 2;
				}
				else
				{
					miniCount = (match.b.teamMembers.Count / 2) + 1;
				}

				foreach (PlayerHelicopter mini in match.minicopters)
				{
					if (mini == null || mini.IsDestroyed)
					{
						minicoptersDestroyed.Add (mini);
					}
				}

				if (match.minicopters.Count != miniCount)
				{
					match.b.points += (miniCount - minicoptersDestroyed.Count) * -15;
				}
			}


			if (match.a.color)
			{
				match.a.points += match.a.crateHackWins * 15;
			}
			else
			{
				if (match.a.teamMembers.Count % 2 == 0)
				{
					miniCount = match.a.teamMembers.Count / 2;
				}
				else
				{
					miniCount = (match.a.teamMembers.Count / 2) + 1;
				}

				foreach (PlayerHelicopter mini in match.minicopters)
				{
					if (mini == null || mini.IsDestroyed)
					{
						minicoptersDestroyed.Add (mini);
					}
				}

				if (match.minicopters.Count != miniCount)
				{
					match.a.points += (miniCount - minicoptersDestroyed.Count) * -15;
				}
			}
		}

		private void SubscribeHooks(bool flag){
			if (flag){
				Subscribe (nameof (OnPlayerConnected));
				Subscribe (nameof (OnPlayerDisconnected));
				Subscribe (nameof (OnEntityDeath));
				Subscribe (nameof (OnTeamKick));
				Subscribe (nameof (OnTeamLeave));
				Subscribe (nameof (OnCrateHackEnd));
			}
			else{
				Unsubscribe (nameof (OnPlayerConnected));
				Unsubscribe (nameof (OnPlayerDisconnected));
				Unsubscribe (nameof (OnEntityDeath));
				Unsubscribe (nameof (OnTeamKick));
				Unsubscribe (nameof (OnTeamLeave));
				Unsubscribe (nameof (OnCrateHackEnd));
			}
		}
		#endregion
	}
}