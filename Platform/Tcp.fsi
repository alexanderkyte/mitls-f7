﻿module Tcp

open Bytes
open Error

type NetworkStream 
type TcpListener 

(* Create a network stream from a given stream.
   Only used by the application interface TLSharp. *)

val create: System.IO.Stream -> NetworkStream

(* Server side *)

val listen: string -> int -> TcpListener
val acceptTimeout: int -> TcpListener -> NetworkStream
val accept: TcpListener -> NetworkStream
val stop: TcpListener -> unit

(* Client side *)

val connectTimeout: int -> string -> int -> NetworkStream
val connect: string -> int -> NetworkStream

(* Input/Output *)

// val dataAvailable: NetworkStream -> bool Result
val read: NetworkStream -> int -> (string,bytes) OptResult
val write: NetworkStream -> bytes -> (string,unit) OptResult
val close: NetworkStream -> unit
