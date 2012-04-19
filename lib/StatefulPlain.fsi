module StatefulPlain
open Bytes
open Formats
open TLSInfo
open DataStream
open AEADPlain

type data = bytes

type prehistory
type history  = (nat * prehistory)
type fragment

val emptyHistory: KeyInfo -> history
val addToHistory: KeyInfo -> history -> data -> range -> fragment -> history

val makeAD: KeyInfo -> history -> data -> AEADPlain.data

val fragment: KeyInfo -> history -> data -> range -> bytes -> fragment
val repr:     KeyInfo -> history -> data -> range -> fragment -> bytes

val contents:  KeyInfo -> history -> data -> range -> fragment -> Fragment.fragment
val construct: KeyInfo -> history -> data -> range -> Fragment.fragment -> fragment

val FragmentToAEADPlain: KeyInfo -> history -> data -> range -> fragment -> AEADPlain
val AEADPlainToFragment: KeyInfo -> history -> data -> range -> AEADPlain -> fragment