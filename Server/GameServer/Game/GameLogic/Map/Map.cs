﻿using Google.Protobuf.Protocol;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameServer
{
	public struct PQNode : IComparable<PQNode>
	{
		public int F;
		public int G;
		public int X;
		public int Y;

		public int CompareTo(PQNode other)
		{
			if (F == other.F)
				return 0;
			return F < other.F ? 1 : -1;
		}
	}

	// 지형과 오브젝트 충돌 및 A* 길찾기를 종합적으로 관리
	public class MapComponent
	{
		public int MinX { get; set; }
		public int MaxX { get; set; }
		public int MinY { get; set; }
		public int MaxY { get; set; }

		public int SizeX { get { return MaxX - MinX + 1; } }
		public int SizeY { get { return MaxY - MinY + 1; } }

		// 맵 자체적으로 충돌이 있는지(벽, 장애물) 체크하기 위함
		bool[,] _mapCollision;

		// 특정 위치에 오브젝트가 있는지 체크하기 위함
		BaseObject[,] _objCollision;

		public bool CanGo(BaseObject self, Vector2Int cellPos, bool checkObjects = true)
		{
			int extraCells = 0;
			if (self != null)
				extraCells = self.ExtraCells;

			for (int dx = -extraCells; dx <= extraCells; dx++)
			{
				for (int dy = -extraCells; dy <= extraCells; dy++)
				{
					Vector2Int checkPos = new Vector2Int(cellPos.x + dx, cellPos.y + dy);
					if (CanGo_Internal(self, checkPos, checkObjects) == false) // CellPos
					{
						return false;
					}
				}
			}

			return true;
		}

		bool CanGo_Internal(BaseObject self, Vector2Int cellPos, bool checkObjects = true)
		{
			if (cellPos.x < MinX || cellPos.x > MaxX)
				return false;
			if (cellPos.y < MinY || cellPos.y > MaxY)
				return false;

			int x = cellPos.x - MinX;           
			int y = MaxY - cellPos.y;

			// 충돌 영역면 볼 것도 없이 못 간다
			if (_mapCollision[x, y])
				return false;

			if (checkObjects)
			{
				// 자신이 아닌 다른 오브젝트가 있으면 못 간다
				if (_objCollision[x, y] != null && _objCollision[x, y] != self)
					return false;
			}

			return true;
		}

		public BaseObject FindObjectAt(Vector2Int cellPos)
		{
			if (cellPos.x < MinX || cellPos.x > MaxX)
				return null;
			if (cellPos.y < MinY || cellPos.y > MaxY)
				return null;

			return _objCollision[cellPos.x, cellPos.y];
		}

		public bool ApplyLeave(BaseObject obj)
		{
			if (obj.Room == null)
				return false;
			if (obj.Room.Map != this)
				return false;

			PositionInfo posInfo = obj.PosInfo;
			if (posInfo.PosX < MinX || posInfo.PosX > MaxX)
				return false;
			if (posInfo.PosY < MinY || posInfo.PosY > MaxY)
				return false;

			// Zone
			Zone zone = obj.Room.GetZone(obj.CellPos);
			zone.Remove(obj);

			// 충돌이 있는 애라면 기존의 좌표 제거
			EGameObjectType type = obj.ObjectType;

			{
                int x = posInfo.PosX - MinX;
                int y = MaxY - posInfo.PosY;

                int extraCells = obj.ExtraCells;
				for (int dx = -extraCells; dx <= extraCells; dx++)
				{
					for (int dy = -extraCells; dy <= extraCells; dy++)
					{
						if (_objCollision[x + dx, y + dy] == obj)
							_objCollision[x + dx, y + dy] = null;
					}
				}
			}

			return true;
		}

		public bool ApplyMove(BaseObject obj, Vector2Int dest, bool checkObjects = true, bool collision = true)
		{
			if (obj == null)
				return false;
			if (obj.Room == null)
				return false;
			if (obj.Room.Map != this)
				return false;

			int extraCells = obj.ExtraCells;

			PositionInfo posInfo = obj.PosInfo;
			if (CanGo(obj, dest, checkObjects) == false)
				return false;

			if (collision)
			{
				// 기존의 좌표 제거
				{
					int x = posInfo.PosX - MinX;
					int y = MaxY - posInfo.PosY;

					for (int dx = -extraCells; dx <= extraCells; dx++)
					{
						for (int dy = -extraCells; dy <= extraCells; dy++)
						{
							if (_objCollision[x + dx, y + dy] == obj)
								_objCollision[x + dx, y + dy] = null;
						}
					}
				}
				// 새로운 좌표 추가
				{
					int x = dest.x - MinX;
					int y = MaxY - dest.y;

					for (int dx = -extraCells; dx <= extraCells; dx++)
					{
						for (int dy = -extraCells; dy <= extraCells; dy++)
						{
							_objCollision[x + dx, y + dy] = obj;
						}
					}
				}
			}

			// Zone
			EGameObjectType type = ObjectManager.GetObjectTypeFromId(obj.ObjectId);
			if (type == EGameObjectType.Hero)
			{
				Hero hero = (Hero)obj;
				Zone now = obj.Room.GetZone(obj.CellPos);
				Zone after = obj.Room.GetZone(dest);
				if (now != after)
				{
					now.Heroes.Remove(hero);
					after.Heroes.Add(hero);
				}
			}

			// 실제 좌표 이동
			posInfo.PosX = dest.x;
			posInfo.PosY = dest.y;

			return true;
		}

		public void LoadMap()
		{
			string mapName = "MMO_edu_mapCollision"; // TEMP

			// Collision 관련 파일
			string text = File.ReadAllText($"{ConfigManager.Config.dataPath}/MapData/{mapName}.txt");
			StringReader reader = new StringReader(text);

			MinX = int.Parse(reader.ReadLine());
			MaxX = int.Parse(reader.ReadLine());
			MinY = int.Parse(reader.ReadLine());
			MaxY = int.Parse(reader.ReadLine());

			int xCount = MaxX - MinX + 1;
			int yCount = MaxY - MinY + 1;
			_mapCollision = new bool[xCount, yCount];
			_objCollision = new BaseObject[xCount, yCount];

			for (int y = 0; y < yCount; y++) 
			{
				string line = reader.ReadLine();
				for (int x = 0; x < xCount; x++)
				{
					switch (line[x])
					{
						case Define.MAP_TOOL_WALL:
							_mapCollision[x, y] = true;
							break;
						default:
							_mapCollision[x, y] = false;
							break;
					}
				}
			}
		}

		#region A* PathFinding

		public struct PQNode : IComparable<PQNode>
		{
			public int H; // Heuristic
			public Vector2Int CellPos;
			public int Depth;

			public int CompareTo(PQNode other)
			{
				if (H == other.H)
					return 0;
				return H < other.H ? 1 : -1;
			}
		}

		List<Vector2Int> _delta = new List<Vector2Int>()
		{
			new Vector2Int(0, 1), // U
			new Vector2Int(1, 1), // UR
			new Vector2Int(1, 0), // R
			new Vector2Int(1, -1), // DR
			new Vector2Int(0, -1), // D
			new Vector2Int(-1, -1), // LD
			new Vector2Int(-1, 0), // L
			new Vector2Int(-1, 1), // LU
		};

		public List<Vector2Int> FindPath(BaseObject self, Vector2Int startCellPos, Vector2Int destCellPos, int maxDepth = 10)
		{
			// 지금까지 제일 좋은 후보 기록.
			Dictionary<Vector2Int, int> best = new Dictionary<Vector2Int, int>();
			// 경로 추적 용도.
			Dictionary<Vector2Int, Vector2Int> parent = new Dictionary<Vector2Int, Vector2Int>();

			// 현재 발견된 후보 중에서 가장 좋은 후보를 빠르게 뽑아오기 위한 도구.
			PriorityQueue<PQNode> pq = new PriorityQueue<PQNode>(); // OpenList

			Vector2Int pos = startCellPos;
			Vector2Int dest = destCellPos;

			// destCellPos에 도착 못하더라도 제일 가까운 애로.
			Vector2Int closestCellPos = startCellPos;
			int closestH = (dest - pos).sqrMagnitude;

			// 시작점 발견 (예약 진행)
			{
				int h = (dest - pos).sqrMagnitude;
				pq.Push(new PQNode() { H = h, CellPos = pos, Depth = 1 });
				parent[pos] = pos;
				best[pos] = h;
			}

			while (pq.Count > 0)
			{
				// 제일 좋은 후보를 찾는다
				PQNode node = pq.Pop();
				pos = node.CellPos;

				// 목적지 도착했으면 바로 종료.
				if (pos == dest)
					break;

				// 무한으로 깊이 들어가진 않음.
				if (node.Depth >= maxDepth)
					break;

				// 상하좌우 등 이동할 수 있는 좌표인지 확인해서 예약한다.
				foreach (Vector2Int delta in _delta)
				{
					Vector2Int next = pos + delta;

					// 갈 수 없는 장소면 스킵.
					if (CanGo(self, next) == false)
						continue;

					// 예약 진행
					int h = (dest - next).sqrMagnitude;

					// 더 좋은 후보 찾았는지
					if (best.ContainsKey(next) == false)
						best[next] = int.MaxValue;

					if (best[next] <= h)
						continue;

					best[next] = h;

					pq.Push(new PQNode() { H = h, CellPos = next, Depth = node.Depth + 1 });
					parent[next] = pos;

					// 목적지까지는 못 가더라도, 그나마 제일 좋았던 후보 기억.
					if (closestH > h)
					{
						closestH = h;
						closestCellPos = next;
					}
				}
			}

			// 제일 가까운 애라도 찾음.
			if (parent.ContainsKey(dest) == false)
				return CalcCellPathFromParent(parent, closestCellPos);

			return CalcCellPathFromParent(parent, dest);
		}

		List<Vector2Int> CalcCellPathFromParent(Dictionary<Vector2Int, Vector2Int> parent, Vector2Int dest)
		{
			List<Vector2Int> cells = new List<Vector2Int>();

			if (parent.ContainsKey(dest) == false)
				return cells;

			Vector2Int now = dest;

			while (parent[now] != now)
			{
				cells.Add(now);
				now = parent[now];
			}

			cells.Add(now);
			cells.Reverse();

			return cells;
		}

		#endregion
	}
}
