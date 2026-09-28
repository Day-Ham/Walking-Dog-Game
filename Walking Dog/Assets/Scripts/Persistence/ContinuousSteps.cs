using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// A cumulative stream per account on this installation. Walk boundaries never
// reset it. Cloud acknowledgements use the stream's absolute total, not batches.
public sealed class ContinuousSteps
{
    [Serializable]
    public sealed class Record
    {
        public int version = 1;
        public string owner = "";
        public string streamId = Guid.NewGuid().ToString("N");
        public long total;
        public long acknowledged;
    }

    private readonly string directory;
    private readonly Dictionary<string, Record> records = new Dictionary<string, Record>();
    private int highWater;
    private string lastOwner;
    public string Error { get; private set; } = "";
    public ContinuousSteps(string directory) { this.directory = directory; }
    public void BeginSensorSource() { highWater = 0; }

    public Record Get(string owner)
    {
        owner = owner ?? "";
        if (records.TryGetValue(owner, out var record)) return record;
        var path = PathFor(owner);
        record = Read(path, owner) ?? Read(path + ".bak", owner);
        if (record == null && (File.Exists(path) || File.Exists(path + ".bak")))
        {
            Error = "Saved steps unavailable";
            throw new IOException("Saved step totals are unreadable; files have been preserved.");
        }
        record = record ?? new Record { owner = owner };
        records.Add(owner, record);
        return record;
    }

    public void Observe(string owner, int sensorSteps, bool legacyWalk = false)
    {
        owner = owner ?? "";
        sensorSteps = Math.Max(0, sensorSteps);
        var delta = Math.Max(0, sensorSteps - highWater);
        highWater = Math.Max(highWater, sensorSteps);
        // Never move steps across accounts. Guest steps remain guest-owned.
        if (lastOwner != null && lastOwner != owner) delta = 0;
        lastOwner = owner;
        if (delta == 0 || legacyWalk) return;
        try
        {
            var record = Get(owner);
            record.total = checked(record.total + delta);
            Save(record);
            Error = "";
        }
        catch (Exception) { Error = "Step saving pending"; }
    }

    public void Flush(string owner)
    {
        try { Save(Get(owner)); Error = ""; }
        catch (Exception) { Error = "Step saving pending"; }
    }

    // Persist before submission, so a server commit never outruns local durability.
    public Record Snapshot(string owner)
    {
        var record = Get(owner);
        Save(record);
        return new Record { owner = record.owner, streamId = record.streamId,
            total = record.total, acknowledged = record.acknowledged };
    }

    public void Acknowledge(Record uploaded)
    {
        var record = Get(uploaded.owner);
        if (record.streamId != uploaded.streamId) return;
        record.acknowledged = Math.Max(record.acknowledged, Math.Min(record.total, uploaded.total));
        Save(record);
    }

    private string PathFor(string owner)
    {
        using (var hash = SHA256.Create())
            return Path.Combine(directory, BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(owner))).Replace("-", "") + ".json");
    }

    private static Record Read(string path, string owner)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var value = JsonUtility.FromJson<Record>(File.ReadAllText(path));
            return value != null && value.version == 1 && value.owner == owner
                && Guid.TryParseExact(value.streamId, "N", out _) && value.total >= 0
                && value.total <= PointsWalletSnapshot.Maximum && value.acknowledged >= 0
                && value.acknowledged <= value.total ? value : null;
        }
        catch (Exception) { return null; }
    }

    private void Save(Record record)
    {
        Directory.CreateDirectory(directory);
        var path = PathFor(record.owner);
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(record));
        using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        if (File.Exists(path)) File.Replace(path + ".tmp", path, Read(path, record.owner) != null ? path + ".bak" : null);
        else File.Move(path + ".tmp", path);
    }
}
