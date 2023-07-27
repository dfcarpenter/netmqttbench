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

let writeLineToCsv (time, total, rate) fileName =
    let line = $"{time},{total},{rate}\n"
    File.AppendAllText(fileName, line)

// let runJob pub pubArgs sub subArgs timeoutSeconds =
//     async {
//         let! results =
//             [runCliUtilityWithTimeout pub pubArgs timeoutSeconds
//              runCliUtilityWithTimeout sub subArgs timeoutSeconds]
//              |> Async.Parallel
//         let! child = Async.StartChild (fun () ->
//             results
//             |> List.map processLines
//             |> List.iter (fun line -> writeLineToCsv line "job_outputs.csv"))
//         return! child
//     }

// type JobStatus =
//     | Success of int * string * string
//     | Failure of int * string * string * exn

// let runJob index pub pubArgs sub subArgs timeoutSeconds =
//     async {
//         try
//             let! results = runCliUtilityWithTimeout pub pubArgs timeoutSeconds |> Async.RunSynchronously
//             let! results2 = runCliUtilityWithTimeout sub subArgs timeoutSeconds |> Async.RunSynchronously
//             return Success(index, results, results2)
//         with
//         | ex -> 
//             printfn "Job failed with exception: %s" ex.Message
//             return Failure(index, pub, sub, ex)
//     }

// let main args =
//     printfn "Starting bench jobs"
//     let jobStatuses =
//         jobs
//         |> List.mapi (fun index (u1, a1, u2, a2, t) -> runJob index u1 a1 u2 a2 t |> Async.RunSynchronously)
//     let failedJobs = jobStatuses |> List.choose (function Failure(i, _, _, _) as failure -> Some(i, failure) | _ -> None)
//     printfn "Retrying failed jobs"
//     failedJobs
//     |> List.iter (fun (index, _) -> let (u1, a1, u2, a2, t) = jobs.[index] in runJob index u1 a1 u2 a2 t |> Async.RunSynchronously |> ignore)
//     0 // return an integer exit code



// let runCliUtilityAndStreamOutput utility args =
//     async {
//         let proc = new Process()

//         proc.StartInfo.FileName <- utility
//         proc.StartInfo.Arguments <- args
//         proc.StartInfo.UseShellExecute <- false
//         proc.StartInfo.RedirectStandardOutput <- true

//         // Set up the OutputDataReceived event handler
//         proc.OutputDataReceived.Add(fun args ->
//             match args.Data with
//             | null -> () // Ignore null data, which signals the end of output
//             | line -> 
//                 // Print the line to the console
//                 printfn "%s" line

//                 // Save the line to the list
//                 output.Add(line))

//         proc.Start() |> ignore

//         // Begin asynchronous read of the output
//         proc.BeginOutputReadLine()

//         let! exitCode = proc.WaitForExitAsync() |> Async.AwaitTask

//         return exitCode
//     }

// // Run the function
// runCliUtilityAndStreamOutput "ls" "-l" 
// |> Async.RunSynchronously 
// |> ignore
// printfn "Output lines: %A" output

// At this point, 'output' will contain all lines of output from the process

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