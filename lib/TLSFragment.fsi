﻿module TLSFragment

open Bytes
open TLSInfo
open TLSConstants

type prehistory
type history = prehistory

type fragment =
    | FHandshake of HSFragment.fragment
    | FCCS of HSFragment.fragment
    | FAlert of HSFragment.fragment
    | FAppData of AppFragment.fragment

val emptyHistory: epoch -> history
val addToHistory: epoch -> ContentType -> history -> range -> fragment -> history

//val historyStream: epoch -> ContentType -> history -> stream

val fragmentPlain: epoch -> ContentType -> history -> range -> bytes -> fragment
val fragmentRepr:     epoch -> ContentType -> history -> range -> fragment -> bytes


//val contents:  epoch -> ContentType -> history -> range -> fragment -> Fragment.fragment
//val construct: epoch -> ContentType -> history -> range -> Fragment.fragment -> fragment
