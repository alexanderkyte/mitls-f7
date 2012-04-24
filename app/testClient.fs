﻿module testClient

open Error
open Dispatch

let serverIP = "google.com" // "rigoletto.polito.it" //  // 128.93.188.162
let serverPort = 443
let options = TLSInfo.defaultProtocolOptions

let rec consume conn =
    match TLS.read conn with
    | ReadError e ->
        match e with
        | EInternal (x,y) ->
            Printf.printf "AYEEE!!! %A %A" x y
            ignore (System.Console.ReadLine())
            None
        | EFatal x ->
            Printf.printf "AYEEE!!! %A" x
            ignore (System.Console.ReadLine())
            None
    | Handshaken (conn) ->
        Printf.printf "Full OK"
        ignore (System.Console.ReadLine())
        Some(conn)
    | DontWrite (conn) ->
        consume conn
    | x ->
        Printf.printf "AYEEE!!! %A" x
        ignore (System.Console.ReadLine())
        None

let testCl options =
    let ns = Tcp.connect serverIP serverPort in
    let conn = TLS.connect ns options in
    ignore (consume conn)

let test = 
    SessionDB.create options
    testCl options

let testRes options sid =
    let ns = Tcp.connect serverIP serverPort in
    printf "Asking resumption with %A" sid
    match SessionDB.select options sid with
    | None -> printf "AYEEE, expecting to resume a session!"
    | Some (sinfo) ->
        let conn = TLS.resume ns sid options in
        match conn with
        | Error(x,y) -> Printf.printf "AYEEE!!! %A %A" x y
        | Correct(conn) ->
            match consume conn with
            | None -> ()
            | Some(conn) ->
                let sinfo = TLS.getSessionInfo (TLS.getEpochIn conn) in
                match sinfo.sessionID with
                | None -> printf "Full handshake, and got new, non-resumable session."
                | Some (newSid) ->
                    if sid = newSid then
                        Printf.printf "Resumption OK"
                    else
                        printf "Gotta Full handshake"
        ignore (System.Console.ReadLine ())

(*
let testFullAndReKey () =
    match testCl options with
    | (Error(x,y),_,_) -> ()
    | (_,conn,ns) ->
    let sinfo = TLS.getSessionInfo conn in
    match sinfo.sessionID with
    | None -> printf "Non resumable session. Sorry."
    | Some (sid) ->
        printfn "Asking re-keying"
        match TLS.rekey_now conn options with
        | (Error(x,y),_) -> Printf.printf "AYEEE!!! %A %A" x y
        | Correct(_), conn ->
            let sinfo = TLS.getSessionInfo conn in
            match sinfo.sessionID with
            | None -> printf "Full handshake, and got new, non-resumable session."
            | Some (newSid) ->
                if sid = newSid then
                    Printf.printf "Re-keying OK"
                else
                    printf "Gotta Full handshake"
        ignore (System.Console.ReadLine ())
*)

(*
let testFullAndRehandshake () =
    match testCl options with
    | (Error(x,y),_,_) -> ()
    | (_,conn,ns) ->
        printfn "Asking re-handshake"
        match TLS.rehandshake_now conn options with
        | (Error(x,y),_) -> Printf.printf "AYEEE!!! %A %A" x y
        | Correct(_), conn ->
            printf "Full re-handshake OK"
            ignore (System.Console.ReadLine ())
*)

(*
let testResumptionRollbackAttack () =
    (* Do a full new session in TLS 1.1 *)
    let ops = {options with minVer = CipherSuites.TLS_1p1
                            maxVer = CipherSuites.TLS_1p1
                            safe_renegotiation = false} in
    match testCl ops with
    | (Error(x,y),_,_) -> ()
    | (_,conn,ns) ->
        let sinfo = TLS.getSessionInfo conn in
        (* TODO: we might want to close the current connection,
           but closure is still not handled in our implementation *)
        Tcp.close ns
        (* Cheat in our sinfo information, changing the protocol version to TLS 1.0 *)
        let sinfo = {sinfo with protocol_version = CipherSuites.TLS_1p0 }
        match sinfo.sessionID with
        | None -> printf "Impossible to resume session."
        | Some (sid) ->
            SessionDB.insert ops sid sinfo
            let ops = {options with minVer = Formats.TLS_1p0
                                    maxVer = Formats.TLS_1p0} in
            testRes ops sid
*)
        