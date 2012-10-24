﻿module Bytes

type nat = int 
type bytes = byte[]
type lbytes = bytes

val createBytes: int -> int -> bytes

val bytes_of_int: int -> int -> bytes

val int_of_bytes: bytes -> int

val length: bytes -> int

val equalBytes: bytes -> bytes -> bool

val mkRandom: int -> bytes

(* append *)
val (@|): bytes -> bytes -> bytes
val split: bytes -> int -> (bytes * bytes)
val split2: bytes -> int -> int -> (bytes * bytes * bytes)
(* strings *)
val utf8: string -> bytes
val iutf8: bytes -> string

(* Time spans *)
type DateTime
type TimeSpan
val now: unit -> DateTime
val newTimeSpan: nat -> nat -> nat -> nat -> TimeSpan
val addTimeSpan: DateTime -> TimeSpan -> DateTime
val greaterDateTime: DateTime -> DateTime -> bool