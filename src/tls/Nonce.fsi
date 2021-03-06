﻿(* Copyright (C) 2012--2014 Microsoft Research and INRIA *)

#light "off"

module Nonce

open Bytes

val random: nat -> bytes
val mkHelloRandom: unit -> bytes

val noCsr: bytes
