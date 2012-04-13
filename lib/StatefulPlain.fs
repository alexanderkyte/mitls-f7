module StatefulPlain
open Bytes
open Formats
open TLSInfo
open DataStream

type data = bytes

type prehistory =
    | Empty
    | ConsHistory of prehistory * data * range * sbytes
type history = (nat * prehistory)

type prefragment = sbytes
type fragment = {contents: prefragment}

let emptyHistory (ki:KeyInfo) = (0,Empty)
let addToHistory (ki:KeyInfo) (seqn,h) d r x = (seqn+1,ConsHistory(h,d,r,x.contents))

let makeAD (ki:KeyInfo) ((seqn,h):history) ad =
  let bn = bytes_of_seq seqn in
    bn @| ad

let fragment (ki:KeyInfo) (h:history) (ad:data) (r:range) (b:bytes) = {contents = plain ki r b}

let repr (ki:KeyInfo) (h:history) (ad:data) (r:range) (f:fragment) = repr ki r f.contents

let contents  (ki:KeyInfo) (h:history) (ad:data) (rg:range) f = f.contents
let construct (ki:KeyInfo) (h:history) (ad:data) (rg:range) c = {contents = c}

let FragmentToAEADPlain ki h ad r f =
    AEADPlain.construct ki r (makeAD ki h ad) f.contents

let AEADPlainToFragment ki h ad r p =
    let f = AEADPlain.contents ki r (makeAD ki h ad) p in
    {contents = f}


