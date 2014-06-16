﻿module DH

open Bytes
open DHGroup

type secret

//Restricting the interface to the minimum
//val gen_pp     : unit -> p * g
//val default_pp : unit -> p * g

//val genKey: p -> g -> elt * secret
//val exp: p -> g -> elt -> elt -> secret -> PMS.dhpms

val serverGen: unit -> p * g * elt * secret
val clientGenExp: p -> g -> elt -> (elt * secret * PMS.dhpms)
val serverExp: p -> g -> elt -> elt -> secret -> PMS.dhpms 
