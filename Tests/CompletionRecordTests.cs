using System;
using System.IO;
using Warp.Tools;
using Warp.Workers.Queue;
using Xunit;

namespace Tests;

public class CompletionRecordTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "completion_record_" + Guid.NewGuid().ToString("N"));
    private string RecordDir => Path.Combine(_dir, "completed");
    private string MetaPath => Path.Combine(_dir, "item.xml");

    public CompletionRecordTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(MetaPath, "<TiltSeries ProcessingStatus=\"1\" />");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static TaskItem Task(int index, string path, double defocus, string stage = "preprocess") => new TaskItem
    {
        TaskId = $"{index:D7}-ctf-{Path.GetFileNameWithoutExtension(path)}",
        Stage = stage,
        Main = new[] { new NamedSerializableObject("TomoProcessCTF", path, defocus) },
    };

    [Fact]
    public void UnrecordedTaskIsNotComplete()
    {
        Assert.False(CompletionRecord.IsComplete(RecordDir, Task(0, "a.tomostar", 2.0), MetaPath));
    }

    [Fact]
    public void RecordedTaskWithUnchangedMetadataIsComplete()
    {
        var t = Task(0, "a.tomostar", 2.0);
        CompletionRecord.MarkComplete(RecordDir, t, MetaPath);
        Assert.True(CompletionRecord.IsComplete(RecordDir, t, MetaPath));
    }

    [Fact]
    public void IndexInTaskIdDoesNotMatterButWorkDoes()
    {
        CompletionRecord.MarkComplete(RecordDir, Task(0, "a.tomostar", 2.0), MetaPath);

        // Same work at a different position in the input list: still done.
        Assert.True(CompletionRecord.IsComplete(RecordDir, Task(7, "a.tomostar", 2.0), MetaPath));
        // Different options, item, or stage: not done.
        Assert.False(CompletionRecord.IsComplete(RecordDir, Task(0, "a.tomostar", 2.5), MetaPath));
        Assert.False(CompletionRecord.IsComplete(RecordDir, Task(0, "b.tomostar", 2.0), MetaPath));
        Assert.False(CompletionRecord.IsComplete(RecordDir, Task(0, "a.tomostar", 2.0, stage: "other"), MetaPath));
    }

    [Fact]
    public void ChangedMetadataInvalidatesTheRecord()
    {
        var t = Task(0, "a.tomostar", 2.0);
        CompletionRecord.MarkComplete(RecordDir, t, MetaPath);

        // Another command (or a re-run of an earlier step) rewrote the item's metadata.
        File.WriteAllText(MetaPath, "<TiltSeries ProcessingStatus=\"1\" AlignmentChanged=\"1\" />");
        Assert.False(CompletionRecord.IsComplete(RecordDir, t, MetaPath));
    }

    [Fact]
    public void MissingMetadataIsNeverComplete()
    {
        var t = Task(0, "a.tomostar", 2.0);
        CompletionRecord.MarkComplete(RecordDir, t, MetaPath);
        File.Delete(MetaPath);
        Assert.False(CompletionRecord.IsComplete(RecordDir, t, MetaPath));
    }

    [Fact]
    public void NoRecordIsWrittenWithoutMetadata()
    {
        var t = Task(0, "a.tomostar", 2.0);
        File.Delete(MetaPath);
        CompletionRecord.MarkComplete(RecordDir, t, MetaPath);
        Assert.False(File.Exists(CompletionRecord.RecordPath(RecordDir, t)));
    }

    [Fact]
    public void ClearRemovesTheRecord()
    {
        var t = Task(0, "a.tomostar", 2.0);
        CompletionRecord.MarkComplete(RecordDir, t, MetaPath);
        CompletionRecord.Clear(RecordDir, t);
        Assert.False(CompletionRecord.IsComplete(RecordDir, t, MetaPath));
    }

    [Fact]
    public void CorruptRecordIsNotComplete()
    {
        var t = Task(0, "a.tomostar", 2.0);
        CompletionRecord.MarkComplete(RecordDir, t, MetaPath);
        File.WriteAllText(CompletionRecord.RecordPath(RecordDir, t), "{ not json");
        Assert.False(CompletionRecord.IsComplete(RecordDir, t, MetaPath));
    }

    [Fact]
    public void FingerprintIsStableAcrossCalls()
    {
        Assert.Equal(CompletionRecord.Fingerprint(Task(0, "a.tomostar", 2.0)),
                     CompletionRecord.Fingerprint(Task(3, "a.tomostar", 2.0)));
    }
}
