﻿module PwApp

open Bytes
open PwToken
open Dispatch

type username = PwToken.username

val request  : (*servname*)string -> username -> token -> Connection option
val response : (*servname*)string -> (string * Connection) option
