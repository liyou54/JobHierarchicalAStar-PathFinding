using Script.PathFind;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

public class MapdataTestMenu
{
    [MenuItem("Test/GroupIdGen")]
    public static void GenGroupId()
    {
        var chunkId = new int2(63, 63);
        var batchId = new int2(3, 3);
        var offset = 5;
        var lod = 6;
        var id = GroupHelper.GenGroupId(chunkId, batchId, offset, lod);
        Debug.Log(GroupHelper.GetLod(id));
        Debug.Log(GroupHelper.GetChunkId(id));
        Debug.Log(GroupHelper.GetBatchId(id));
        Debug.Log(GroupHelper.GetOffset(id));
        Debug.Log(id);
    }

    public static MapData TestMapData()
    {
        var mapSize = 4;

        var mapDataInfo = new MapDataInfo()
        {
            AllGroupShape = new int2(1 << mapSize, 1 << mapSize),
            StartLod = 2,
            MaxLod = 3,
            ObstacleShape = new int2((1 << mapSize) - 1, (1 << mapSize) - 1)
        };


        var mapData = new MapData(mapDataInfo);
        var obstacleMap = new NativeArray<ObstacleType>(mapDataInfo.ObstacleShape.ToSize(), Allocator.Persistent);
        obstacleMap[7] = ObstacleType.Tree;
        obstacleMap[15 + 7] = ObstacleType.Tree;
        obstacleMap[15 * 2 + 7] = ObstacleType.Tree;
        obstacleMap[15 * 3 + 7] = ObstacleType.Tree;
        obstacleMap[15 * 3 + 6] = ObstacleType.Water;

        obstacleMap[1] = ObstacleType.Tree;

        obstacleMap[3] = ObstacleType.Hard;
        obstacleMap[15 + 3] = ObstacleType.Hard;
        obstacleMap[15 * 2 + 3] = ObstacleType.Hard;
        obstacleMap[15 * 3 + 3] = ObstacleType.Tree;
        obstacleMap[15 * 4 + 3] = ObstacleType.Hard;
        obstacleMap[15 * 5 + 3] = ObstacleType.Hard;
        obstacleMap[15 * 6 + 3] = ObstacleType.Hard;
        obstacleMap[15 * 7 + 3] = ObstacleType.Hard;
        mapData.Build(obstacleMap);
        return mapData;
    }

    public static void TryLoadMap(TextAsset mapData, out NativeArray<ObstacleType> data, out int2 size)
    {
        var text = mapData.text;
        var lines = text.Split('\n');
        var height = int.Parse(lines[1].Replace("height ", ""));
        var weight = int.Parse(lines[2].Replace("width ", ""));
        size = new int2(weight, height);
        data = new NativeArray<ObstacleType>(weight * height, Allocator.Persistent);
        for (int y = 0; y < height; y++)
        {
            var line = lines[y + 4];
            for (int x = 0; x < weight; x++)
            {
                var index = y * weight + x;
                var c = line[x];
                if (c == '@')
                {
                    data[index] = ObstacleType.Hard;
                }
                else if (c == 'T')
                {
                    data[index] = ObstacleType.Water;
                }
                else
                {
                    data[index] = ObstacleType.Default;
                }

            }
        }
    }

    [MenuItem("Test/BuildFirstStep")]
    public static void BuildFirstStep()
    {
        TestMapData();
    }
}