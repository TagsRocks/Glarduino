﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Glarduino
{
	public sealed class DebugLogUnityGlarduinoClient : MonoBehaviour
	{
		[SerializeField]
		[Tooltip("Port name with which the SerialPort object will be created.")]
		private string portName = "COM3";

		[SerializeField]
		[Tooltip("Baud rate that the serial device is using to transmit data.")]
		private int baudRate = 9600;

		private IDisposable CurrentClient;

		private async Task Start()
		{
			UnityStringGlarduinoClient client = new UnityStringGlarduinoClient(new ArduinoPortConnectionInfo(portName, baudRate), new StringMessageDeserializerStrategy(), new DebugLogStringMessageDispatchingStrategy());

			CurrentClient = client;
			client.ConnectionEvents.OnClientConnected += (sender, args) => Debug.Log($"Port: {portName} connected.");
			client.ConnectionEvents.OnClientDisconnected += (sender, args) => Debug.Log($"Port: {portName} disconnected.");

			await client.ConnectAsync();
			await client.StartListeningAsync();
		}

		void OnDisable()
		{
			//When disabled we should just dispose.
			CurrentClient?.Dispose();
		}

		/// <summary>
		/// See: https://docs.unity3d.com/ScriptReference/MonoBehaviour.OnApplicationQuit.html
		/// </summary>
		void OnApplicationQuit()
		{
			//When disabled we should just dispose.
			CurrentClient?.Dispose();
		}
	}
}