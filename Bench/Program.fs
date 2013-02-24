﻿// FIXME: use performance counters

open System
open System.IO
open System.Net
open Microsoft.FSharp.Reflection

open Org.BouncyCastle.Crypto.Prng

open TLStream

let port  = 5000
let block = 256 * 1024

(* ------------------------------------------------------------------------ *)
exception NotAValidEnumeration

let enumeration<'T> () =
    let t = typeof<'T>

    if not (FSharpType.IsUnion(t)) then
        raise NotAValidEnumeration;

    let cases = FSharpType.GetUnionCases(t)

    if not (Array.forall
                (fun (c : UnionCaseInfo) -> c.GetFields().Length = 0)
                (FSharpType.GetUnionCases(t))) then
        raise NotAValidEnumeration;

    let cases =
        Array.map
            (fun (c : UnionCaseInfo) ->
                (c.Name, FSharpValue.MakeUnion(c, [||]) :?> 'T))
            cases
    in
        cases

(* ------------------------------------------------------------------------ *)
let cs_map = Map.ofArray (enumeration<TLSConstants.cipherSuiteName> ())
let vr_map = Map.ofArray (enumeration<TLSConstants.ProtocolVersion> ())

(* ------------------------------------------------------------------------ *)
let unnull dfl x =
    if x = null then dfl else x

(* ------------------------------------------------------------------------ *)
type options = {
    certname    : string;
    ciphersuite : TLSConstants.cipherSuiteName;
}

(* ------------------------------------------------------------------------ *)
let tlsconfig options = {
    TLSInfo.minVer = TLSConstants.TLS_1p0
    TLSInfo.maxVer = TLSConstants.TLS_1p0

    TLSInfo.ciphersuites = TLSConstants.cipherSuites_of_nameList [options.ciphersuite]

    TLSInfo.compressions = [ TLSConstants.NullCompression ]

    TLSInfo.honourHelloReq = TLSInfo.HRPResume
    TLSInfo.allowAnonCipherSuite = false
    TLSInfo.check_client_version_in_pms_for_old_tls = true
    TLSInfo.request_client_certificate = false
    
    TLSInfo.safe_renegotiation = true

    TLSInfo.server_name = options.certname;
    TLSInfo.client_name = ""

    TLSInfo.sessionDBFileName = "sessionDBFile.bin"
    TLSInfo.sessionDBExpiry   = Bytes.newTimeSpan 1 0 0 0 (* one day *)
}

(* ------------------------------------------------------------------------ *)
let udata = Array.create (1024*1024) 0uy

let initialize_udata () =
    let rnd = new CryptoApiRandomGenerator() in
        rnd.NextBytes(udata, 0, udata.Length)

(* ------------------------------------------------------------------------ *)
let server (listener : Sockets.TcpListener) config =
    use socket = listener.AcceptTcpClient () in
    let stream = new TLStream (socket.GetStream (), config, TLStream.TLSServer, false) in
    let buffer = Array.create block 0uy in
    let loop   = ref true in
        while !loop do
            if stream.Read (buffer, 0, buffer.Length) = 0 then
                loop := false
        done

(* ------------------------------------------------------------------------ *)
let client config =
    use socket = new Sockets.TcpClient () in

    socket.Connect (new IPEndPoint(IPAddress.Loopback, 5000));

    let stream = new TLStream (socket.GetStream (), config, TLStream.TLSClient, false) in

    let sent  = ref 0 in
    let upos  = ref 0 in
    let ticks = DateTime.Now.Ticks in

        while !sent < 64*1024*1024 do
            if udata.Length - !upos < block then begin
                upos := 0
            end;
            stream.Write (udata, !upos, block)
            sent := !sent + block
        done;
        let ticks = DateTime.Now.Ticks - ticks in
            stream.Close ();
            (!sent, ticks)

(* ------------------------------------------------------------------------ *)
let entry () =
    let ciphersuite =
        unnull "TLS_RSA_WITH_AES_128_CBC_SHA"
            (Environment.GetEnvironmentVariable ("CIPHERSUITE"))

    let certname =
        unnull "rsa.cert-01.mitls.org"
            (Environment.GetEnvironmentVariable ("CERTNAME"))

    let options = {
        certname    = certname;
        ciphersuite = Map.find ciphersuite cs_map;
    }

    initialize_udata ()
    let listener = new Sockets.TcpListener(IPAddress.Loopback, 5000) in

    listener.Start ();

    let client   = async { return (client (tlsconfig options)) } in
    let server   = async { server listener (tlsconfig options) } in
        Async.Start server;
        let sent, ticks = Async.RunSynchronously client in
        let rate = float(sent) / (float(ticks) / float(TimeSpan.TicksPerSecond)) in
            printfn "%s: %f MiB/s" ciphersuite (rate / (1024. * 1024.))

(* ------------------------------------------------------------------------ *)
let _ = entry ()
