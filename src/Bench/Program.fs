// For more information see https://aka.ms/fsharp-console-apps

open System.Diagnostics
open System.Threading 

let runCliUtilityWithTimeout utility args timeoutSeconds =
    async {
        let proc = new Process()

        proc.StartInfo.FileName <- utility
        proc.StartInfo.Arguments <- args
        proc.StartInfo.UseShellExecute <- false
        proc.StartInfo.RedirectStandardOutput <- true

        let timeout = timeoutSeconds * 1000

        proc.Start() |> ignore 
        let timer = new Timer((fun _ -> proc.Kill()), null, timeout, Timeout.Infinite)
        let! output = proc.StandardOutput.ReadToEndAsync() |> Async.AwaitTask

        timer.Dispose()
        return output 
    }

type JobStatus = 
    | Success of string * string 
    | Failure of string * string * exn 

    
let runJob pub pubArgs sub subArgs timeoutSeconds =
    async {
        let! results =
            [runCliUtilityWithTimeout pub pubArgs timeoutSeconds
             runCliUtilityWithTimeout sub subArgs timeoutSeconds]
             |> Async.Parallel
            
        return results
    }


let processLines lines =
    let splitLine (line : string) =
        let parts = line.Split(' ')
        let time = parts.[0]
        let total = parts.[3].Split('=')[1]
        let rate = parts.[4].Split('=')[1]
        (time, total, rate)
    
    let parsedLines = lines |> List.map splitLine
    let lastLine = List.last parsedLines
    let totalRate = parsedLines |> List.sumBy (fun (_, _, rate) -> float rate)
    let averageRate = totalRate / float (List.length parsedLines)

    (lastLine, averageRate)    

let jobs =
    [("emqtt_bench", "pub -c 1 -h 192.168.1.34 -t bench/%i -I 0 -s 16 -q 1", "emqtt_bench", "sub -c 1 -h 192.168.1.34 -t bench/# -q 1", 30);
     ("emqtt_bench", "pub -c 1 -h 192.168.1.34 -t bench/%i -I 0 -s 8192 -q 1", "emqtt_bench", "sub -c 1 -h 192.168.1.34 -t bench/# -q 1", 30);]

[<EntryPoint>]
let main args =
    printfn "Starting bench jobs"
    jobs
    |> Seq.map(fun (u1, a1, u2, a2, t) -> runJob u1 a1 u2 a2 t |> Async.RunSynchronously)
    |> Seq.iter(fun outputs ->
        printfn "Pub: %s" outputs.[0]
        printfn "Sub: %s" outputs.[1])
    0 // return an integer exit code