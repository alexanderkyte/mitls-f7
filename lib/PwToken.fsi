﻿module PwToken

// ------------------------------------------------------------------------
open Bytes
open TLSInfo
open DataStream

// ------------------------------------------------------------------------
type token
type username = string

val create   : unit -> token
val register : username -> token -> unit
val verify   : username -> token -> bool
val guess    : bytes -> token

// ------------------------------------------------------------------------
type delta = DataStream.delta

val tk_repr  : epoch -> stream -> username -> token -> delta
val tk_plain : epoch -> stream -> range -> delta -> (username * token) option
