using System;
using UnityEngine;

namespace Sim.Logging {
    public static class LoggingExamples {
        
        public static void ExamplePlayerEnteredRoom(string playerId, string roomId) {
            GameLogger.Rooms.Info("PlayerEnteredRoom {PlayerId} {RoomId}", playerId, roomId);
        }

        public static void ExamplePlayerLeftRoom(string playerId, string roomId, float duration) {
            GameLogger.Rooms.Info("PlayerLeftRoom {PlayerId} {RoomId} {DurationSeconds}", 
                playerId, roomId, duration);
        }

        public static void ExamplePropSpawned(int propId, string roomId, int prefabId, string propType) {
            GameLogger.Props.Info("PropSpawned {PropId} {RoomId} {PrefabId} {Type}", 
                propId, roomId, prefabId, propType);
        }

        public static void ExamplePropUpdated(int propId, string roomId, Vector3 position, Quaternion rotation) {
            GameLogger.Props.Debug("PropUpdated {PropId} {RoomId} Position={Position} Rotation={Rotation}", 
                propId, roomId, position, rotation);
        }

        public static void ExamplePropDestroyed(int propId, string roomId, string reason) {
            GameLogger.Props.Info("PropDestroyed {PropId} {RoomId} {Reason}", 
                propId, roomId, reason);
        }

        public static void ExamplePropError(int propId, Exception ex) {
            GameLogger.Props.Error(ex, "Error while updating prop {PropId}", propId);
        }

        public static void ExampleNetworkClientConnected(int connectionId, string ipAddress) {
            GameLogger.Network.Info("ClientConnected {ConnectionId} {IpAddress}", 
                connectionId, ipAddress);
        }

        public static void ExampleNetworkClientDisconnected(int connectionId, string reason) {
            GameLogger.Network.Info("ClientDisconnected {ConnectionId} {Reason}", 
                connectionId, reason);
        }

        public static void ExampleNetworkMessageReceived(string messageType, int connectionId, int payloadSize) {
            GameLogger.Network.Debug("MessageReceived {MessageType} {ConnectionId} {PayloadSize}", 
                messageType, connectionId, payloadSize);
        }

        public static void ExampleNetworkError(Exception ex, string context) {
            GameLogger.Network.Error(ex, "Network error in {Context}", context);
        }

        public static void ExamplePlayerAction(string playerId, string action, string target) {
            GameLogger.Player.Info("PlayerAction {PlayerId} {Action} {Target}", 
                playerId, action, target);
        }

        public static void ExamplePlayerInventoryChanged(string playerId, string itemId, int quantity) {
            GameLogger.Player.Debug("PlayerInventoryChanged {PlayerId} {ItemId} {Quantity}", 
                playerId, itemId, quantity);
        }

        public static void ExampleRoomCreated(string roomId, string ownerId, int maxPlayers) {
            GameLogger.Rooms.Info("RoomCreated {RoomId} {OwnerId} {MaxPlayers}", 
                roomId, ownerId, maxPlayers);
        }

        public static void ExampleRoomDestroyed(string roomId, int playerCount, float lifetime) {
            GameLogger.Rooms.Info("RoomDestroyed {RoomId} {PlayerCount} {LifetimeSeconds}", 
                roomId, playerCount, lifetime);
        }

        public static void ExampleSystemStartup(string version, string buildDate) {
            GameLogger.System.Info("ServerStartup {Version} {BuildDate}", version, buildDate);
        }

        public static void ExampleSystemShutdown(string reason, int activeConnections) {
            GameLogger.System.Info("ServerShutdown {Reason} {ActiveConnections}", 
                reason, activeConnections);
        }

        public static void ExamplePerformanceWarning(string component, float processingTime, float threshold) {
            GameLogger.System.Warning("PerformanceWarning {Component} {ProcessingTimeMs} exceeded threshold {ThresholdMs}", 
                component, processingTime, threshold);
        }

        public static void ExampleDatabaseOperation(string operation, string table, int affectedRows, float duration) {
            GameLogger.System.Debug("DatabaseOperation {Operation} {Table} {AffectedRows} {DurationMs}", 
                operation, table, affectedRows, duration);
        }

        public static void ExampleCriticalError(Exception ex, string context) {
            GameLogger.Fatal(ex, "Critical error in {Context}, server may be unstable", context);
        }
    }
}
