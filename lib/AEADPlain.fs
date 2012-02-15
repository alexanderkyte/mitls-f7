module AEADPlain
open Error
open Bytes
open TLSInfo
open DataStream
open StatefulPlain

type addData = bytes
type plain = {p : (state * fragment)}

let plain: KeyInfo -> range -> addData -> bytes -> plain = 
  fun ki r ad b -> 
    let s = emptyState ki in
    {p = (s, fragment ki s ad r b)}

let repr:  KeyInfo -> range -> addData -> plain -> bytes = 
  fun ki r ad pl ->
    let (s,f) = pl.p in 
    StatefulPlain.repr ki s ad r f

let fragmentToPlain: KeyInfo -> state -> addData -> range -> fragment -> plain = 
  fun ki s ad r f -> {p = (s,f)}

let plainToFragment: KeyInfo -> state -> addData -> range -> plain -> fragment = 
  fun ki s ad r p -> 
    let (s',f') = p.p in
      if s = s' then f' 
      else failwith "expected a compatible fragment State"
