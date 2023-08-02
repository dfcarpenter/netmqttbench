// For more information see https://aka.ms/fsharp-console-apps
open System
open System.Diagnostics
open System.Threading 
open System.Threading.Tasks
open System.IO
open Argu 

let runCliUtilityWithTimeout utility args timeoutSeconds =
    task {
        use proc = new Process()
        let output = new System.Collections.Generic.List<string>()
        let window = new System.Collections.Generic.Queue<string>()
        let windowAvgs = new System.Collections.Generic.List<float>()

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

                printfn "%s" line

                window.Enqueue(line)
                if window.Count > windowSize then 
                    let _ = window.Dequeue() 
                
                // Calculate the average rate for the current window
                let rates = 
                    windows 
                    |> Seq.map (fun line -> float (line.Split('=').[1]))
                    |> Seq.toArray
                let avgRate = rates |> Array.average 

                // Add the average rate to the list of window averages 
                windowAvgs.Add(avgRate)
                if windowAvgs.Count > 5 then
                    let _ = windowAvgs.RemoveAt(0)

                let mean = windowAvgs |> List.average
                let stdDev = Math.Sqrt(windowAvgs |> List.map (fun x -> (x - mean) ** 2.0) |> List.average)
                
                if Math.Abs(avgRate - mean) <= stdDev then
                    output.Add(avgRate))
               
                

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
            do! proc.WaitForExitAsync()

            if proc.ExitCode <> 0 then
                return Error (sprintf "Process exited with code %d" proc.ExitCode)
            else
                return Ok(output.ToArray())
        finally 

            if not proc.HasExited then proc.Kill()
            proc.Dispose()
    }



let runJob testName pub pubArgs sub subArgs timeoutSeconds =
    task {
        let! results =
            [runCliUtilityWithTimeout pub pubArgs timeoutSeconds
             runCliUtilityWithTimeout sub subArgs timeoutSeconds]
             |> Task.WhenAll

        match results with 
        | [| Ok pubOutput; Ok subOutput|] ->
            return Ok (testName, pubOutput, subOutput)
        | [| Error err; _ |] ->
            return Error (sprintf "Error in pub: %s" err)
        | [| _; Error err |] ->
            return Error (sprintf "Error in sub: %s" err)
        | _ ->
            return Error "Unexpected error" 
        
    }

let jobs =
    [("1 sub 1 pub payload 16 qos 1", "emqtt_bench", "pub -c 1 -h 192.168.1.34 -t bench/%i -I 0 -s 16 -q 1", "emqtt_bench", "sub -c 1 -h 192.168.1.34 -t bench/# -q 1", 30);
     ("1 sub 1 pub payload 16 qos 1","emqtt_bench", "pub -c 1 -h 192.168.1.34 -t bench/%i -I 0 -s 16 -q 1", "emqtt_bench", "sub -c 1 -h 192.168.1.34 -t bench/# -q 1", 30);]


let processLines lines =

    let removeInitial (str: string) =
        str.Split('\n')
        |> Array.skip 2 
        |> String.concat "\n"
    let splitLine (line : string) =
        let parts = line.Split(' ')
        let time = parts.[0]
        let total = parts.[3].Split('=')[1]
        let rate = parts.[4].Split('=')[1]
        (time, total, rate)
    
    let modifiedLines =
        lines 
        |> removeInitial
        |> fun str -> str.Split('\n')
        |> Array.toList
    let parsedLines = modifiedLines |> List.map splitLine
    let lastLine = List.last parsedLines
    let totalRate = parsedLines |> List.sumBy (fun (_, _, rate) -> float rate)
    let averageRate = totalRate / float (List.length parsedLines)

    (lastLine, averageRate)    


// type CliArguments =
//     | Host of string 
//     | Port of tcp_port:int
//     | Duration of int 
//     | FilePath of path:string 

//     interface IArgParserTemplate with 
//         member s.Usage =
//             match s with 
//             | Host _ -> "Host to connect to"
//             | Port _ -> "Port to connect to"
//             | Duration _ -> "Duration of the test in minutes"

[<EntryPoint>]
let main args =
    // let argv = Environment.GetCommandLineArgs()
    // let parser = new ArgumentParser<CliArguments>(programName = "mqtt_bench")

    // let args =
    //     try 
    //         parser.Parse(argv)
    //     with 
    //     | :? ArguException as e ->
    //         printfn "%s" (e.Message)
    //         Environment.Exit 1 |> ignore
    //         Unchecked.defaultof<_>

    printfn "Starting bench jobs"
   
    jobs 
    |> List.map (fun (name, u1, a1, u2, a2, t) ->
        match Task.Run(fun () -> runJob name u1 a1 u2 a2 t).Result with
        | Ok (name, pubOutput, subOutput) ->
            printfn "Pub: %A" pubOutput
        | Error err -> 
            printfn "Error: %s" err)
    |> ignore
    0 // return an integer exit code