using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// v1.1.0 — 7주차(로그 CSV 저장) + 9/10주차(get_event_history/get_log) 지원용 로봇별 이벤트 기록기입니다.
/// MonoBehaviour가 아닌 순수 C# 클래스로 만들어 RobotController가 인스턴스별로 하나씩 소유합니다.
/// </summary>
public class EventLogger
{
    private struct Record
    {
        public string timestamp;
        public int robotId;
        public float x;
        public float z;
        public string eventType;
        public string eventValue;
    }

    private readonly int agentId;
    private readonly List<Record> records = new List<Record>();
    private StreamWriter csvWriter;

    public EventLogger(int agentId)
    {
        this.agentId = agentId;
    }

    public void Add(float x, float z, string eventType, string eventValue)
    {
        var record = new Record
        {
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            robotId = agentId,
            x = x,
            z = z,
            eventType = eventType,
            eventValue = eventValue
        };
        records.Add(record);
        WriteCsvIfEnabled(record);
    }

    public void SetCsvEnabled(bool enabled)
    {
        if (enabled && csvWriter == null)
        {
            OpenCsvFile();
        }
        else if (!enabled && csvWriter != null)
        {
            csvWriter.Flush();
            csvWriter.Close();
            csvWriter = null;
        }
    }

    private void OpenCsvFile()
    {
        try
        {
            // 실행 폴더(.exe와 같은 위치) 하위 logs/ 에 저장 — 문서에 명시된 저장 경로
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "logs");
            Directory.CreateDirectory(dir);
            string fileName = $"robot_log_{DateTime.Now:yyyyMMdd_HHmmss}_agent{agentId}.csv";
            csvWriter = new StreamWriter(Path.Combine(dir, fileName), false);
            csvWriter.WriteLine("timestamp,robot_id,x,z,event_type,event_value");
            csvWriter.Flush();
        }
        catch (Exception e)
        {
            Debug.Log($"[EventLogger] CSV 파일 생성 실패: {e.Message}");
        }
    }

    private void WriteCsvIfEnabled(Record r)
    {
        if (csvWriter == null) return;
        try
        {
            csvWriter.WriteLine($"{r.timestamp},{r.robotId},{r.x:F2},{r.z:F2},{r.eventType},\"{r.eventValue}\"");
            csvWriter.Flush();
        }
        catch (Exception e)
        {
            Debug.Log($"[EventLogger] CSV 기록 실패: {e.Message}");
        }
    }

    public string RecentAsJson(int limit)
    {
        int count = Mathf.Min(limit, records.Count);
        var slice = records.Skip(records.Count - count).ToList();
        return ToJson(slice);
    }

    public string AllAsJson()
    {
        return ToJson(records);
    }

    private string ToJson(List<Record> list)
    {
        var items = list.Select(r =>
            $"{{\"timestamp\":\"{r.timestamp}\",\"robot_id\":{r.robotId},\"x\":{r.x:F2},\"z\":{r.z:F2},\"event_type\":\"{r.eventType}\",\"event_value\":\"{r.eventValue}\"}}"
        );
        return "[" + string.Join(",", items) + "]";
    }

    public void Clear()
    {
        records.Clear();
    }
}
