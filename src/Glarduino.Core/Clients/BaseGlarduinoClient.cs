﻿using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Glarduino
{
	/// <summary>
	/// Base type for any Glarduino Arduino connected client.
	/// </summary>
	public abstract class BaseGlarduinoClient<TMessageType> : IClientConnectable, IClientListenable, IDisposable
	{
		private ConnectionEvents _ConnectionEvents { get; }

		/// <summary>
		/// The internally managed <see cref="SerialPort"/> that represents
		/// the potentially connected port to the Aurdino device.
		/// </summary>
		protected ICommunicationPort InternallyManagedPort { get; } = null;

		/// <summary>
		/// The connection info used for the <see cref="InternallyManagedPort"/>.
		/// </summary>
		protected ArduinoPortConnectionInfo ConnectionInfo { get; }

		/// <summary>
		/// Indicates if the client is connected.
		/// </summary>
		public virtual bool isConnected => InternallyManagedPort.IsOpen;

		/// <summary>
		/// Container for subscribable connection events for the client.
		/// </summary>
		public IConnectionEventsSubscribable ConnectionEvents => _ConnectionEvents;

		/// <summary>
		/// Strategy for deserializing messages from the serial port.
		/// </summary>
		private IMessageDeserializerStrategy<TMessageType> MessageDeserializer { get; }

		/// <summary>
		/// Strategy for dispatching messages.
		/// </summary>
		private IMessageDispatchingStrategy<TMessageType> MessageDispatcher { get; }

		/// <summary>
		/// Publisher for exceptions encountered
		/// </summary>
		public event EventHandler<Exception> OnExceptionEncountered;

		/// <summary>
		/// Crates a new <see cref="BaseGlarduinoClient{TMessageType}"/>.
		/// </summary>
		/// <param name="connectionInfo">The connection information for the port.</param>
		/// <param name="messageDeserializer">The message deserialization strategy.</param>
		/// <param name="messageDispatcher">The message dispatching strategy.</param>
		protected BaseGlarduinoClient(ArduinoPortConnectionInfo connectionInfo, 
			IMessageDeserializerStrategy<TMessageType> messageDeserializer, 
			IMessageDispatchingStrategy<TMessageType> messageDispatcher)
			: this(connectionInfo, messageDeserializer, messageDispatcher, new SerialPortCommunicationPortAdapter(CreateNewSerialPort(connectionInfo)))
		{

		}

		private static SerialPort CreateNewSerialPort(ArduinoPortConnectionInfo connectionInfo)
		{
			//Address issues in underlying stream opening: http://zachsaw.blogspot.com/2010/07/net-serialport-woes.html
			SerialPortFixer.Execute(connectionInfo.PortName);
			return new SerialPort(connectionInfo.PortName, connectionInfo.BaudRate);
		}

		/// <summary>
		/// Crates a new <see cref="BaseGlarduinoClient{TMessageType}"/>.
		/// Specifically defining the communication port.
		/// </summary>
		/// <param name="connectionInfo">The connection information for the port.</param>
		/// <param name="messageDeserializer">The message deserialization strategy.</param>
		/// <param name="messageDispatcher">The message dispatching strategy.</param>
		/// <param name="comPort">The specified communication port.</param>
		protected BaseGlarduinoClient(ArduinoPortConnectionInfo connectionInfo,
			IMessageDeserializerStrategy<TMessageType> messageDeserializer,
			IMessageDispatchingStrategy<TMessageType> messageDispatcher,
			ICommunicationPort comPort)
		{
			ConnectionInfo = connectionInfo ?? throw new ArgumentNullException(nameof(connectionInfo));
			MessageDeserializer = messageDeserializer ?? throw new ArgumentNullException(nameof(messageDeserializer));
			MessageDispatcher = messageDispatcher ?? throw new ArgumentNullException(nameof(messageDispatcher));
			InternallyManagedPort = comPort ?? throw new ArgumentNullException(nameof(comPort));

			_ConnectionEvents = new ConnectionEvents();
		}

		/// <inheritdoc />
		public Task<bool> ConnectAsync(CancellationToken cancelToken = default(CancellationToken))
		{
			InternallyManagedPort.ReadTimeout = ConnectionInfo.ReadTimeout;
			InternallyManagedPort.WriteTimeout = ConnectionInfo.WriteTimeout;

			try
			{
				InternallyManagedPort.Open(cancelToken);
			}
			catch (Exception e)
			{
				throw new InvalidOperationException($"Failed to connect Glarduino to Port: {ConnectionInfo.PortName}. Reason: {e.Message}", e);
			}

			//Alert subscribers that the client has connected.
			if (InternallyManagedPort.IsOpen)
				_ConnectionEvents.InvokeClientConnected();

			return Task.FromResult(InternallyManagedPort.IsOpen);
		}

		/// <inheritdoc />
		public async Task StartListeningAsync(CancellationToken cancelToken = default(CancellationToken))
		{
			try
			{
				if (!isConnected)
					throw new InvalidOperationException($"Cannot start listening with {nameof(StartListeningAsync)} when internal {nameof(SerialPort)} {InternallyManagedPort} is not connected/open.");

				while (isConnected && !cancelToken.IsCancellationRequested)
				{
					//TODO: Handle cancellation tokens better.
					TMessageType message = await MessageDeserializer.ReadMessageAsync(InternallyManagedPort, cancelToken)
						.ConfigureAwait(false);

					//Don't dispatch if canceled.
					if (cancelToken.IsCancellationRequested || !isConnected)
						return;

					//A message was recieved and deserialized as the strategy implemented.
					//Therefore we should assume we have a valid message and then pass it to the message dispatching strategy.
					await MessageDispatcher.DispatchMessageAsync(message)
						.ConfigureAwait(false);
				}
			}
			catch (Exception e)
			{
				OnExceptionEncountered?.Invoke(this, e);
				throw;
			}
			finally
			{
				Dispose();

				//TODO: Should we assume disconnection just because listening stopped?
				_ConnectionEvents.InvokeClientDisconnected();
			}
		}

		/// <inheritdoc />
		public virtual void Dispose()
		{
			//We just dispose of the port.
			if (InternallyManagedPort.IsOpen)
				InternallyManagedPort?.Close();
				
			InternallyManagedPort?.Dispose();
		}
	}
}
