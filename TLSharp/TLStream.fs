﻿module TLStream

open Bytes
open Tcp
open Error
open System
open System.IO

type TLSBehavior =
    | TLSClient
    | TLSServer

type TLStream(s:System.Net.Sockets.NetworkStream, options, b) =
    inherit Stream()
    let mutable inbuf:bytes = [||]
    let mutable outbuf:bytes = [||]
    let mutable closed:bool = true

    let doMsg_o conn b =
        let ki = TLS.getEpochOut conn
        let s = TLS.getOutStream conn
        let l = length b
        (l,l),DataStream.createDelta ki s (l,l) b

    let undoMsg_i conn (r,d) =
        let ki = TLS.getEpochIn conn
        let s = TLS.getInStream conn
        DataStream.deltaRepr ki s r d

    let rec doHS conn =
        match TLS.read conn with
        | TLS.ReadError (err) ->
            match err with
            | EInternal(x,y) -> raise (IOException(sprintf "TLS-HS: Internal error: %A %A" x y))
            | EFatal ad -> raise (IOException(sprintf "TLS-HS: Sent fatal alert: %A" ad))
        | TLS.Close ns -> raise (IOException(sprintf "TLS-HS: Connection closed during HS"))
        | TLS.Fatal ad -> raise (IOException(sprintf "TLS-HS: Received fatal alert: %A" ad))
        | TLS.Warning (conn,ad) -> raise (IOException(sprintf "TLS-HS: Received warning alert: %A" ad))
        | TLS.CertQuery (conn,q) -> raise (IOException(sprintf "TLS-HS: Asked to authorize a certificate"))
        | TLS.Handshaken conn -> closed <- false; conn
        | TLS.Read (conn,msg) ->
            let b = undoMsg_i conn msg
            inbuf <- inbuf @| b
            doHS conn
        | TLS.DontWrite conn -> doHS conn

    let rec wrapRead conn =
        match TLS.read conn with
        | TLS.ReadError (err) ->
            match err with
            | EInternal(x,y) -> raise (IOException(sprintf "TLS-HS: Internal error: %A %A" x y))
            | EFatal ad -> raise (IOException(sprintf "TLS-HS: Sent fatal alert: %A" ad))
        | TLS.Close ns -> closed <- true; (conn,[||]) // FIXME: this is an old connection, should not be used!
        | TLS.Fatal ad -> raise (IOException(sprintf "TLS-HS: Received fatal alert: %A" ad))
        | TLS.Warning (conn,ad) -> raise (IOException(sprintf "TLS-HS: Received warning alert: %A" ad))
        | TLS.CertQuery (conn,q) -> raise (IOException(sprintf "TLS-HS: Asked to authorize a certificate"))
        | TLS.Handshaken conn -> wrapRead conn
        | TLS.Read (conn,msg) -> (conn,undoMsg_i conn msg)
        | TLS.DontWrite conn -> wrapRead conn

    let mutable conn =
        let tcpStream = Tcp.create s
        let conn =
            match b with
            | TLSClient -> TLS.connect tcpStream options
            | TLSServer -> TLS.accept_connected tcpStream options
        doHS conn

    let rec wrapWrite conn msg =
        match TLS.write conn msg with
        | TLS.WriteError err ->
            match err with
            | EInternal(x,y) -> raise (IOException(sprintf "TLS-HS: Internal error: %A %A" x y))
            | EFatal ad -> raise (IOException(sprintf "TLS-HS: Sent alert: %A" ad))
        | TLS.WriteComplete conn -> conn
        | TLS.WritePartial (conn,msg) -> wrapWrite conn msg
        | TLS.MustRead conn ->
            let conn = doHS conn
            wrapWrite conn msg

    override this.get_CanRead()     = true
    override this.get_CanWrite()    = true
    override this.get_CanSeek()     = false
    override this.get_Length()      = raise (NotSupportedException())
    override this.SetLength(i)      = raise (NotSupportedException())
    override this.get_Position()    = raise (NotSupportedException())
    override this.set_Position(i)   = raise (NotSupportedException())
    override this.Seek(i,o)         = raise (NotSupportedException())

    override this.Flush() =
        if not (equalBytes outbuf [||]) then
            let msgO = doMsg_o conn outbuf
            conn <- wrapWrite conn msgO
            outbuf <- [||]

    override this.Read(buffer, offset, count) =
        let data =
            if equalBytes inbuf [||] then
                (* Read from the socket, and possibly buffer some data *)
                let (c,data) = wrapRead conn
                    // Fixme: is data is [||] we should set conn to "null" (which we cannot)
                conn <- c
                data
            else (* Use the buffer *)
                let tmp = inbuf in
                inbuf <- [||]
                tmp
        let l = length data in
        if l <= count then
            Array.blit data 0 buffer offset l
            l
        else
            let (recv,newBuf) = split data count in
            Array.blit recv 0 buffer offset count
            inbuf <- newBuf
            count

    override this.Write(buffer,offset,count) =
        let data = createBytes count 0 in
        Array.blit buffer offset data 0 count
        outbuf <- data
        this.Flush ()

    override this.Close() =
        this.Flush()
        if not closed then
            TLS.half_shutdown conn
            closed <- true
        s.Close()