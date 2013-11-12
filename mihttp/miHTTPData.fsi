﻿module MiHTTPData

open Bytes
open Range
open TLSInfo
open DataStream

type document
type cdocument = (cbytes * cbytes) list * cbytes

val create   : unit -> document
val progress : document -> cbytes -> document
val finalize : document -> cdocument option

val push_delta : epoch -> stream -> range -> delta -> document -> document

val request : epoch -> stream -> range -> string -> delta

