using System;
using System.Collections.Generic;
using System.Diagnostics;
using Script.Job.PathFind;
using Script.PathFind;
using Sirenix.OdinInspector;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace Script.Test
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public class MapDataTest : MonoBehaviour
    {
        public MapData MapData;
        public TextAsset MapDataAsset;
        public int StartLod => MapData.MapDataInfo.StartLod;
        public int MaxLod => MapData.MapDataInfo.MaxLod;

        public Material Material;

        [PropertyRange("StartLod", "MaxLod")] public int DebugLod;
        private List<Texture2D> GroupDataList = new List<Texture2D>();

        public struct EdgeDebug
        {
            public float4 Edge;
            public ObstacleType ObstacleType;
        }

        private List<NativeList<EdgeDebug>> EdgeDataList = new List<NativeList<EdgeDebug>>();
        private NativeList<GroupId> FindPathRes;

        private MeshRenderer MeshRenderer;
        private MeshFilter MeshFilter;

        private void BuildTempLod()
        {
            foreach (var groupData in GroupDataList)
            {
                DestroyImmediate(groupData);
            }


            GroupDataList.Clear();

            for (int i = StartLod; i < MaxLod + 1; i++)
            {
                var groupData = new Texture2D(MapData.MapDataInfo.AllGroupShape.x, MapData.MapDataInfo.AllGroupShape.y);
                groupData.filterMode = FilterMode.Point;
                GroupDataList.Add(groupData);
            }

            for (int i = 0; i < MapData.MapDataInfo.AllGroupShape.ToSize(); i++)
            {
                var lastGroupId = MapData.FirstLodGroupIdIndexMap[i];
                for (int j = StartLod; j < MaxLod + 1; j++)
                {
                    if (lastGroupId.IsValid())
                    {
                        var data = GroupDataList[j - StartLod];
                        var color = GroupIdToDebugColor(lastGroupId);
                        var coordX = i / MapData.MapDataInfo.AllGroupShape.x;
                        var coordY = i % MapData.MapDataInfo.AllGroupShape.x;
                        data.SetPixel(coordY, coordX, color);
                        lastGroupId = MapData.GroupInfoMap[lastGroupId].ParentGroupId;
                    }
                }
            }

            for (int j = StartLod; j < MaxLod + 1; j++)
            {
                GroupDataList[j - StartLod].Apply();
            }
        }

        public void InitEdgeList()
        {
            foreach (var edge in EdgeDataList)
            {
                edge.Dispose();
            }

            EdgeDataList.Clear();
            for (int i = StartLod; i < MaxLod + 1; i++)
            {
                var edge = new NativeList<EdgeDebug>(MapData.MapDataInfo.AllGroupShape.ToSize(), Allocator.Persistent);
                EdgeDataList.Add(edge);
            }


            var hashSet = new HashSet<GroupId>();

            foreach (var key in MapData.EdgeMap.GetKeyArray(Allocator.Temp))
            {
                var repeat = new NativeHashSet<EdgeInfo>(16, Allocator.Temp);

                if (hashSet.Contains(key))
                {
                    continue;
                }

                hashSet.Add(key);

                foreach (var value in MapData.EdgeMap.GetValuesForKey(key))
                {
                    Debug.Assert(repeat.Contains(value) == false, $"repeat {key} edge {value}");
                    repeat.Add(value);
                    var lod = GroupHelper.GetLod(value.SrcGroupId);
                    var srcPos = MapData.GroupInfoMap[value.SrcGroupId].BatchCellCoordPosition;
                    var dstPos = MapData.GroupInfoMap[value.DstGroupId].BatchCellCoordPosition;
                    var edge = new float4(srcPos.x, srcPos.y, dstPos.x, dstPos.y) + .5f;
                    var edgeLod = EdgeDataList[lod - StartLod];
                    var obstacleType = value.ObstacleType;
                    var edgeDebug = new EdgeDebug
                    {
                        Edge = edge,
                        ObstacleType = obstacleType
                    };
                    edgeLod.Add(edgeDebug);
                }
            }

            var tempLod = StartLod;
            foreach (var edge in EdgeDataList)
            {
                Debug.Log($"Lod {tempLod++} edge count {edge.Length}");
            }
        }

        [Button]
        public void InitMap()
        {
            Init = false;
            MapData.Dispose();
            MeshRenderer = GetComponent<MeshRenderer>();
            MeshFilter = GetComponent<MeshFilter>();
            MapdataTestMenu.TryLoadMap(MapDataAsset, out var data, out var size);
            var mapDataInfo = new MapDataInfo(size, 4, 7);
            MapData = new MapData(mapDataInfo);
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            MapData.Build(data);
            stopwatch.Stop();
            Debug.Log($"Build map data cost {stopwatch.ElapsedMilliseconds} ms");
            gameObject.transform.localScale = new Vector3(MapData.MapDataInfo.AllGroupShape.x, MapData.MapDataInfo.AllGroupShape.y, 1);
            gameObject.transform.position = new Vector3(MapData.MapDataInfo.AllGroupShape.x / 2f, MapData.MapDataInfo.AllGroupShape.y / 2f, 0);
            MeshRenderer.sharedMaterial = Material;
            InitEdgeList();
            BuildTempLod();
            Init = true;
        }

        public int2 FindStart = new int2(0, 0);
        public int2 FindEnd = new int2(0, 0);

        [Button]
        public void FindPath()
        {
            var startPos = new Position((ushort)FindStart.x, (ushort)FindStart.y);
            var endPos = new Position((ushort)FindEnd.x, (ushort)FindEnd.y);

            if (FindPathRes.IsCreated == false)
            {
                FindPathRes = new NativeList<GroupId>(1024, Allocator.Persistent);
            }

            var job = new FindPathAStarJob2(MapData, ObstacleType.Default, FindPathRes, startPos, endPos);
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            job.Schedule().Complete();

            stopwatch.Stop();
            Debug.Log($"Find map data cost {stopwatch.ElapsedMilliseconds} ms");
        }

        public bool Init { get; set; }

        private Color[] DebugColors;

        private Color GroupIdToDebugColor(GroupId groupId)
        {
            if (!groupId.IsValid())
            {
                return Color.black;
            }

            if (DebugColors == null || DebugColors.Length == 0)
            {
                DebugColors = new Color[100];
                for (int i = 0; i < DebugColors.Length; i++)
                {
                    DebugColors[i] = new Color(Random.value, Random.value, Random.value);
                }
            }

            return DebugColors[groupId % DebugColors.Length];
        }

        public void OnDrawGizmos()
        {
            if (MapData.IsInit == false || GroupDataList.Count <= DebugLod - StartLod || !Init)
            {
                return;
            }

            var start = new Vector3(FindStart.x, FindStart.y, 0);
            var end = new Vector3(FindEnd.x, FindEnd.y, 0);

            Gizmos.color = Color.red;

            Gizmos.DrawSphere(start, 3f);
            Gizmos.DrawSphere(end, 3f);
            Material.SetTexture("_MainTex", GroupDataList[DebugLod - StartLod]);
            var edgeData = EdgeDataList[DebugLod - StartLod];
            foreach (var edge in edgeData)
            {
                var src = new Vector3(edge.Edge.x, edge.Edge.y, 0);
                var dst = new Vector3(edge.Edge.z, edge.Edge.w, 0);

                if (edge.ObstacleType == ObstacleType.Water)
                {
                    Handles.color = Color.blue;
                    src.x -= .1f;
                    src.y -= .1f;
                }
                else
                {
                    Handles.color = Color.green;
                    src.x += .1f;
                    src.y += .1f;
                }

                Handles.DrawLine(src, dst);
            }

            Handles.color = Color.red;
            if (FindPathRes.IsCreated)
            {
                for (int i = 0; i < FindPathRes.Length - 1; i++)
                {
                    var start1 = FindPathRes[i];
                    var end1 = FindPathRes[i + 1];
                    var startInfo = MapData.GroupInfoMap[start1];
                    var endInfo = MapData.GroupInfoMap[end1];
                    var startPos = (float2)startInfo.BatchCellCoordPosition.ToInt2() + .5f;
                    var endPos = (float2)endInfo.BatchCellCoordPosition.ToInt2() + .5f;
                    var startVec3 = new Vector3(startPos.x, startPos.y, -1);
                    var endVec3 = new Vector3(endPos.x, endPos.y, -1);
                    Handles.DrawLine(startVec3,endVec3,5f);
                }

            }
 
        }
    }
}