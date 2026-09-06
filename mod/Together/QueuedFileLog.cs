namespace Together;

// Native state is serialized before enqueueing. Workers receive only paths/text.
internal sealed class QueuedFileLog {
    private readonly object gate=new();
    private readonly Queue<(string Path,string Text)> queue=new();
    private Task? worker;
    public string LastError {get;private set;}="";
    public int Pending {get{lock(gate)return queue.Count;}}
    public void Append(string path,string text) {
        if(Pending>=512)Flush(); // Bounded backpressure, never drop transaction evidence.
        lock(gate){queue.Enqueue((path,text));if(worker==null||worker.IsCompleted)worker=Task.Run(Drain);}
    }
    private void Drain() {
        while(true) {
            (string Path,string Text) row;
            lock(gate){if(queue.Count==0){worker=null;return;}row=queue.Peek();}
            try{Directory.CreateDirectory(System.IO.Path.GetDirectoryName(row.Path)!);File.AppendAllText(row.Path,row.Text);}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){lock(gate){LastError=e.GetType().Name;worker=null;}return;}
            lock(gate){queue.Dequeue();LastError="";}
        }
    }
    public void Flush(){Task? active;lock(gate)active=worker;active?.GetAwaiter().GetResult();}
}
