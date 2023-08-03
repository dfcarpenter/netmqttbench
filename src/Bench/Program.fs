// For more information see https://aka.ms/fsharp-console-apps
open System
open System.Diagnostics
open System.Threading 
open System.Collections.Generic
open System.Threading.Tasks
open System.IO
open Argu 

let runCliUtilityWithTimeout utility args timeoutSeconds =
    task {
        use proc = new Process()
        let output = new List<string>()

        proc.StartInfo.FileName <- utility
        proc.StartInfo.Arguments <- args
        proc.StartInfo.UseShellExecute <- false
        proc.StartInfo.RedirectStandardOutput <- true
        proc.StartInfo.RedirectStandardError <- true

        let timeout = timeoutSeconds * 1000

        proc.OutputDataReceived.Add(fun args -> 
            match args.Data with 
            | null -> () // Ignore null data which signals the end of output    
            | line -> 

                output.Add(line))
               
            
        proc.ErrorDataReceived.Add(fun args -> 
            match args.Data with 
            | null -> () // Ignore null data which signals the end of output    
            | line -> 
                printfn "%s" line)
        
        let timer = new Timer((fun _ -> proc.Kill()), null, timeout, Timeout.Infinite)
        let printTimer = new Timer((fun _ -> printfn "%A" output), null, 5000, 5000)
        try 
            proc.Start() |> ignore 
            
            //let! output = proc.StandardOutput.ReadToEndAsync() |> Async.AwaitTask

            proc.BeginOutputReadLine()
            proc.BeginErrorReadLine()
            do! Async.AwaitTask(proc.WaitForExitAsync())

            if proc.ExitCode <> 0 then
                return Error (sprintf "Process exited with code %d" proc.ExitCode)
            else
                return Ok(output.ToArray())
        finally 

            if not proc.HasExited then proc.Kill()
            proc.Dispose()
    }



let runJob testName pub pubArgs sub subArgs timeoutSeconds =
    async {
        let! pubOutput = runCliUtilityWithTimeout pub pubArgs timeoutSeconds |> Async.AwaitTask
        let! subOutput = runCliUtilityWithTimeout sub subArgs timeoutSeconds |> Async.AwaitTask

        match pubOutput, subOutput with 
        | Ok pubOutput, Ok subOutput ->
            return Ok (testName, pubOutput, subOutput)
        | Error err, _ ->
            return Error (sprintf "Error in pub: %s" err)
        | _, Error err ->
            return Error (sprintf "Error in sub: %s" err)
    }
let jobs =
    [("1 sub 1 pub payload 16 qos 1", "emqtt_bench", "pub -c 1 -h 192.168.1.34 -t bench/%i -I 0 -s 16 -q 1", "emqtt_bench", "sub -c 1 -h 192.168.1.34 -t bench/# -q 1", 30);
     ("1 sub 1 pub payload 16 qos 1","emqtt_bench", "pub -c 1 -h 192.168.1.34 -t bench/%i -I 0 -s 16 -q 1", "emqtt_bench", "sub -c 1 -h 192.168.1.34 -t bench/# -q 1", 30);]


// let processTestString lines =

//     let removeInitial (str: string) =
//         str.Split('\n')
//         |> Array.skip 2 
//         |> String.concat "\n"
//     let splitLine (line : string) =
//         let parts = line.Split(' ')
//         let time = parts.[0]
//         let total = parts.[3].Split('=')[1]
//         let rate = parts.[4].Split('=')[1]
//         (time, total, rate)
    
//     let modifiedLines =
//         lines 
//         |> removeInitial
//         |> fun str -> str.Split('\n')
//         |> Array.toList
//     let parsedLines = modifiedLines |> List.map splitLine
//     let lastLine = List.last parsedLines
//     let totalRate = parsedLines |> List.sumBy (fun (_, _, rate) -> float rate)
//     let averageRate = totalRate / float (List.length parsedLines)
//     let avgRateString = string averageRate

//     seq { (lastLine, avgRateString) }

let processTestString lines =
    let splitLine (line : string) =
        let parts = line.Split(' ')
        let time = parts.[0]
        let total = parts.[3].Split('=')[1]
        let rate = parts.[4].Split('=')[1]
        (time, total, rate)

    let parsedLines = lines |> Array.map splitLine
    let lastLine = parsedLines |> Array.last
    let totalRate = parsedLines |> Array.sumBy (fun (_, _, rate) -> float rate)
    let averageRate = totalRate / float (Array.length parsedLines)
    let avgRateString = string averageRate

    seq { (lastLine, avgRateString) }

   


let processAndSaveOutput (name, pubOutput, subOutput) =
    async {
        let pubOutputLines = pubOutput |> processTestString
        let subOutputLines = subOutput |> processTestString

        let filename = sprintf "%s.txt" name 
        use writer = new StreamWriter(filename)
        for line in pubOutputLines do
            writer.WriteLine(sprintf "Pub: %A" line)
        for line in subOutputLines do
            writer.WriteLine(sprintf "Sub: %A" line)
    }

[<EntryPoint>]
let main args =
    printfn "Starting bench jobs"
   
    jobs
    |> List.map (fun (name, u1, a1, u2, a2, t) -> 
        match runJob name u1 a1 u2 a2 t |> Async.RunSynchronously with
        | Ok (name, pubOutput, subOutput) ->
            printfn "Pub: %A" pubOutput
            printfn "Sub: %A" subOutput
            let result = processAndSaveOutput (name, pubOutput, subOutput)
            
            Async.RunSynchronously result
        | Error err ->
            printfn "Error: %s" err)
    |> ignore
    0 // return an integer exit code