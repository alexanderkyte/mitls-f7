module StatefulAEAD

// implemented using AEAD with a sequence number 

open Bytes
open Error
open TLSInfo
open StatefulPlain
open Range

type rw =
    | ReaderState
    | WriterState

type state = { 
  key: AEAD.AEADKey; 
  history: history   
}

type reader = state
type writer = state

let GEN ki =
  let w,r = AEAD.GEN ki in
  let h = emptyHistory ki in
  ( { key = w; history = h},
    { key = r; history = h})  
let COERCE ki (rw:rw) b =
  let k  = AEAD.COERCE ki b in
  let h = emptyHistory ki in
  { key = k; history = h}
let LEAK ki (rw:rw) s = AEAD.LEAK ki s.key

let history (ki:epoch) (rw:rw) s = s.history

type cipher = AEAD.cipher

let encrypt (ki:epoch) (w:writer) (ad0:adata) (r:range) (f:plain) =
  let h = w.history in
  let ad = AEADPlain.makeAD ki h ad0 in
  let p = AEADPlain.StatefulPlainToAEADPlain ki h ad0 r f in
  let k,c = AEAD.encrypt ki w.key ad r p in
  let h = extendHistory ki ad0 h r f in
  let w = {key = k; history = h} in
  (w,c)

let decrypt (ki:epoch) (r:reader) (ad0:adata) (e:cipher) =
  let h = r.history in
  let ad = AEADPlain.makeAD ki h ad0 in
  let res = AEAD.decrypt ki r.key ad e in
  match res with
    | Correct x ->
          let (k,rg,p) = x 
          let f = AEADPlain.AEADPlainToStatefulPlain ki h ad0 rg p 
          let h = extendHistory ki ad0 h rg f 
          let r' = {history = h; key = k}
          correct ((r',rg,f))
    | Error (x,y) -> Error (x,y)
