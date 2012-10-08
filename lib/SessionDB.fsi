﻿module SessionDB

open TLSInfo

type SessionDB
type SessionIndex = sessionID * Role * Cert.hint
type StorableSession = SessionInfo * PRFs.masterSecret

val create: config -> SessionDB
val select: SessionDB -> SessionIndex -> StorableSession option
val insert: SessionDB -> SessionIndex -> StorableSession -> SessionDB
val remove: SessionDB -> SessionIndex -> SessionDB
val getAllStoredIDs: SessionDB -> SessionIndex list