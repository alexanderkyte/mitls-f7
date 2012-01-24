﻿module TLSFragment

open Bytes
open TLSInfo
open Formats

// Plain type for Dispatch
type fragment =
    | FHandshake of Handshake.fragment
    | FCCS of Handshake.ccsFragment
    | FAlert of Alert.fragment
    | FAppData of AppDataPlain.fragment
val repr: KeyInfo -> int -> ContentType -> fragment -> bytes
val fragment: KeyInfo -> int -> ContentType -> bytes -> fragment

// Plain type for AEAD
type addData = bytes
type AEADFragment
val AEADFragment: KeyInfo -> int -> addData -> bytes -> AEADFragment
val AEADRepr: KeyInfo -> int -> addData -> AEADFragment -> bytes

val AEADToDispatch: KeyInfo -> int -> ContentType -> addData -> AEADFragment -> fragment
val DispatchToAEAD: KeyInfo -> int -> ContentType -> addData -> fragment -> AEADFragment