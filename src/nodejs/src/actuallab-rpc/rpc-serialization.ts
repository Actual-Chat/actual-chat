// Serialization for the RPC wire format.
//
// Supports two formats:
//   - Text (json5np): JSON envelope + delimiter-separated JSON args (V3 wire format)
//   - Binary (msgpack6np): V5 binary envelope + MessagePack-encoded args
//
// The format is selected at connection time via the `f=` query parameter.

import { encode as msgpackEncode, decode as msgpackDecode, decodeMulti as msgpackDecodeMulti, type DecodeOptions } from "@msgpack/msgpack";
import type { RpcMessage } from "./rpc-message.js";
import {
  ENVELOPE_DELIMITER,
  ARG_DELIMITER,
  FRAME_DELIMITER,
} from "./rpc-message.js";

// ============================================================
// Text format (json5np) — V3 wire format
// ============================================================

/** Serializes an RpcMessage + args into the json5 wire format. */
export function serializeMessage(message: RpcMessage, args?: unknown[]): string {
  const envelope = JSON.stringify(message);
  if (args === undefined || args.length === 0) {
    return envelope + ENVELOPE_DELIMITER;
  }
  const argsStr = args.map((a) => JSON.stringify(a)).join(ARG_DELIMITER);
  return envelope + ENVELOPE_DELIMITER + argsStr;
}

/** Serializes multiple messages into a single WebSocket frame. */
export function serializeFrame(messages: string[]): string {
  return messages.join(FRAME_DELIMITER);
}

/** Splits a WebSocket text frame into individual message strings. */
export function splitFrame(frame: string): string[] {
  return frame.split(FRAME_DELIMITER);
}

/** Deserializes a single message string into envelope + args. */
export function deserializeMessage(raw: string): { message: RpcMessage; args: unknown[] } {
  const nlIndex = raw.indexOf(ENVELOPE_DELIMITER);
  if (nlIndex === -1) {
    return { message: JSON.parse(raw) as RpcMessage, args: [] };
  }

  const envelopeStr = raw.substring(0, nlIndex);
  const argsStr = raw.substring(nlIndex + 1);
  const message = JSON.parse(envelopeStr) as RpcMessage;

  if (argsStr.length === 0) {
    return { message, args: [] };
  }

  const args = argsStr.split(ARG_DELIMITER).map((s) => {
    try {
      return JSON.parse(s) as unknown;
    } catch {
      return s; // Return raw string if not valid JSON
    }
  });

  return { message, args };
}

// ============================================================
// Binary format (msgpack6np) — V5 wire format
// ============================================================

// MessagePack decode options
const msgpackDecodeOptions: DecodeOptions = {};

const textEncoder = new TextEncoder();
const textDecoder = new TextDecoder();

// --- LEB128 VarUInt helpers ---

function writeVarUint(value: number, out: number[]): void {
  if (value < 0) value = 0;
  do {
    let byte = value & 0x7F;
    value >>>= 7;
    if (value > 0) byte |= 0x80;
    out.push(byte);
  } while (value > 0);
}

function readVarUint(data: Uint8Array, offset: number): { value: number; bytesRead: number } {
  let value = 0, shift = 0, bytesRead = 0;
  do {
    const byte = data[offset + bytesRead];
    value |= (byte & 0x7F) << shift;
    bytesRead++;
    if ((byte & 0x80) === 0) break;
    shift += 7;
  } while (shift < 35); // Max 5 bytes for uint32
  return { value: value >>> 0, bytesRead };
}

// --- Binary helpers ---

function concatUint8Arrays(arrays: Uint8Array[]): Uint8Array {
  let totalLength = 0;
  for (const arr of arrays) totalLength += arr.length;
  const result = new Uint8Array(totalLength);
  let offset = 0;
  for (const arr of arrays) {
    result.set(arr, offset);
    offset += arr.length;
  }
  return result;
}

/**
 * Serializes an RpcMessage + args into V5 binary format (msgpack6np).
 *
 * Layout: [4-byte LE size][envelope][argData]
 * Envelope: [callType|headerCount byte][VarUint relatedId][LVarSpan methodRef][4-byte LE argLen]
 * ArgData: concatenated MessagePack-encoded arguments
 *
 * Size field value includes itself (4 bytes).
 */
export function serializeBinaryMessage(message: RpcMessage, args?: unknown[]): Uint8Array {
  // Build envelope header bytes
  const headerParts: number[] = [];

  // Byte 0: upper 3 bits = CallTypeId, lower 5 bits = HeaderCount (always 0 from TS)
  headerParts.push(((message.CallType ?? 0) << 5) & 0xE0);

  // RelatedId as VarUint
  writeVarUint(message.RelatedId ?? 0, headerParts);

  // MethodRef as LVarSpan (VarUint length + UTF-8 bytes)
  const methodBytes = textEncoder.encode(message.Method ?? "");
  writeVarUint(methodBytes.length, headerParts);

  const headerBuf = new Uint8Array(headerParts);

  // Serialize arguments with MessagePack (concatenated, no polymorphism)
  const argBuffers: Uint8Array[] = [];
  if (args && args.length > 0) {
    for (const arg of args) {
      argBuffers.push(msgpackEncode(arg));
    }
  }
  const argData = argBuffers.length > 0 ? concatUint8Arrays(argBuffers) : new Uint8Array(0);

  // ArgData length as fixed 4-byte LE
  const argLenBuf = new Uint8Array(4);
  new DataView(argLenBuf.buffer).setInt32(0, argData.length, true);

  // V5: NO frame-level size prefix (PersistsMessageSize = false)
  // Assemble: header + methodBytes + argLen + argData
  return concatUint8Arrays([headerBuf, methodBytes, argLenBuf, argData]);
}

/**
 * Deserializes a single V5 binary message starting at `offset`.
 * Returns parsed message, args, and number of bytes consumed.
 */
export function deserializeBinaryMessage(
  data: Uint8Array,
  offset: number,
): { message: RpcMessage; args: unknown[]; bytesRead: number } {
  const view = new DataView(data.buffer, data.byteOffset, data.byteLength);

  // V5: no frame-level size prefix. Parse envelope directly.
  let pos = offset;

  // Byte 0: upper 3 bits = CallTypeId, lower 5 bits = HeaderCount
  const byte0 = data[pos++];
  const callTypeId = (byte0 >> 5) & 0x7;
  const headerCount = byte0 & 0x1F;

  // RelatedId as VarUint
  const relId = readVarUint(data, pos);
  pos += relId.bytesRead;

  // MethodRef as LVarSpan
  const methodLen = readVarUint(data, pos);
  pos += methodLen.bytesRead;
  const methodBytes = data.subarray(pos, pos + methodLen.value);
  const method = textDecoder.decode(methodBytes);
  pos += methodLen.value;

  // ArgData length as fixed 4-byte LE
  const argDataLen = view.getInt32(pos, true);
  pos += 4;

  // Skip headers (if any — TS client doesn't use them)
  if (headerCount > 0) {
    for (let h = 0; h < headerCount; h++) {
      // L1Memory: 1-byte length prefix + key bytes
      const keyLen = data[pos++];
      pos += keyLen;
      // LVarSpan: VarUint length + value bytes
      const valLen = readVarUint(data, pos);
      pos += valLen.bytesRead + valLen.value;
    }
  }

  // Deserialize arguments from argData — multiple concatenated MessagePack values
  const args: unknown[] = [];
  const argEnd = pos + argDataLen;
  if (argDataLen > 0) {
    const argSlice = data.subarray(pos, argEnd);
    // decodeMulti yields each MessagePack value from concatenated buffer
    for (const decoded of msgpackDecodeMulti(argSlice)) {
      args.push(decoded);
    }
    pos = argEnd;
  }

  const message: RpcMessage = {
    Method: method,
    RelatedId: relId.value,
    CallType: callTypeId,
  };

  return { message, args, bytesRead: pos - offset };
}

/**
 * Splits a binary WebSocket frame into individual deserialized messages.
 * V5 with PersistsMessageSize=false: each WebSocket message is one RPC message.
 */
export function splitBinaryFrame(
  frame: Uint8Array,
): Array<{ message: RpcMessage; args: unknown[] }> {
  // No size prefix — the entire frame is a single message
  const { message, args } = deserializeBinaryMessage(frame, 0);
  return [{ message, args }];
}

/**
 * Serializes multiple binary messages into a single WebSocket frame.
 * Simply concatenates the already size-prefixed messages.
 */
export function serializeBinaryFrame(messages: Uint8Array[]): Uint8Array {
  return concatUint8Arrays(messages);
}
