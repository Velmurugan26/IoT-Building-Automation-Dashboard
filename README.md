# IoT-Building-Automation-Dashboard

A robust IoT monitoring system bridging STM32 firmware and a WinUI 3 desktop dashboard. Designed for real-time telemetry, reliable communication, and automated hysteresis-based control.

## 🛠️ Technology Stack
* **Microcontroller:** STM32F401RE Nucleo
* **Firmware:** C (STM32 HAL), Interrupt-driven architecture
* **Desktop App:** WinUI 3 (C#)
* **Communication:** UART (115200 baud), JSON Serialization
* **Visualization:** LiveCharts2

## 🔑 Key Engineering Features
* **Reliability:** Implemented packet sequence numbering and ACK handshaking for integrity.
* **Control Logic:** Automated alarm triggers using a 2°C hysteresis band to prevent threshold flickering.
* **Safety:** Non-blocking hardware interrupts ensure sensor polling does not interfere with system responsiveness.
* **UI/UX:** Dynamic COM port connectivity with real-time charting.

## 📖 Setup
1. **STM32:** Flash `main.c` using STM32CubeIDE.
2. **Dashboard:** Open the Visual Studio solution, restore NuGet packages, and build.
3. **Connect:** Select the device COM port and click "Connect".

---
*Developed for Robotics & AI Engineering coursework.*
