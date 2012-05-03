﻿module TLS

open Bytes
open Error
open Dispatch
open TLSInfo
open Tcp
open DataStream

type ioresult_i =
    | ReadError of alertDescription option
    | Close     of Tcp.NetworkStream
    | Fatal     of alertDescription
    | Warning   of nextCn * alertDescription 
    | CertQuery of nextCn * query
    | Handshaken of Connection
    | Read      of nextCn * msg_i
    | DontWrite of Connection
    
type ioresult_o =
    | WriteError    of alertDescription option
    | WriteComplete of nextCn
    | WritePartial  of nextCn * msg_o
    | MustRead      of Connection

(* Event-driven interface *)

val read     : Connection -> ioresult_i
val write    : Connection -> msg_o -> ioresult_o
val shutdown : Connection -> Connection

val connect : NetworkStream -> protocolOptions -> Connection
val resume  : NetworkStream -> sessionID -> protocolOptions -> Connection Result

val rehandshake : Connection -> protocolOptions -> nextCn
val rekey       : Connection -> protocolOptions -> nextCn
val request     : Connection -> protocolOptions -> nextCn

val accept           : TcpListener   -> protocolOptions -> Connection
val accept_connected : NetworkStream -> protocolOptions -> Connection

val authorize: Connection -> query -> Connection
val refuse:    Connection -> query -> unit

val getEpochIn:  Connection -> epoch
val getEpochOut: Connection -> epoch
val getSessionInfo: epoch -> SessionInfo
val getInStream:  Connection -> stream
val getOutStream: Connection -> stream
