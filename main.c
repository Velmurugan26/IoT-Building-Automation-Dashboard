/**
  * @file    main.c
  * @brief   Building Monitor - Optimized Firmware (Interrupt-Safe)
  */
#include "main.h"
#include <string.h>
#include <stdio.h>

TIM_HandleTypeDef  htim2;
TIM_HandleTypeDef  htim3;
TIM_HandleTypeDef  htim11;
UART_HandleTypeDef huart2;
ADC_HandleTypeDef  hadc1;

int     temperature = 0;
int     humidity    = 0;
int     motion      = 0;
int     pot_angle   = 0;
char    jsonBuf[80];

uint8_t rxByte;
char    rxBuffer[64];
uint8_t rxIndex = 0;

volatile uint8_t read_sensors_flag = 0;
volatile uint8_t cmdReady          = 0;
static uint16_t seq = 0;

#define DHT11_PORT GPIOA
#define DHT11_PIN  GPIO_PIN_1

/* Function Prototypes */
void SystemClock_Config(void);
static void MX_GPIO_Init(void);
static void MX_USART2_UART_Init(void);
static void MX_TIM2_Init(void);
static void MX_TIM3_Init(void);
static void MX_TIM11_Init(void);
static void MX_ADC1_Init(void);
void delay_us(uint16_t us);
void DHT11_Start(void);
uint8_t DHT11_Check_Response(void);
uint8_t DHT11_Read_Byte(void);
void Read_DHT11(void);
void Read_Potentiometer(void);
void Set_Servo_Angle(int angle);

int main(void)
{
  HAL_Init();
  SystemClock_Config();
  MX_GPIO_Init();
  MX_USART2_UART_Init();
  MX_ADC1_Init();
  MX_TIM2_Init();
  MX_TIM3_Init();
  MX_TIM11_Init();

  HAL_TIM_Base_Start(&htim11);
  HAL_TIM_PWM_Start(&htim3, TIM_CHANNEL_1);
  Set_Servo_Angle(0);
  HAL_TIM_Base_Start_IT(&htim2);
  HAL_UART_Receive_IT(&huart2, &rxByte, 1);

  while (1)
  {
    if (read_sensors_flag == 1)
    {
      read_sensors_flag = 0;
      Read_DHT11();
      Read_Potentiometer();
      snprintf(jsonBuf, sizeof(jsonBuf), "{\"seq\":%d,\"t\":%d,\"h\":%d,\"m\":%d,\"p\":%d}\r\n", 
               seq, temperature, humidity, motion, pot_angle);
      HAL_UART_Transmit(&huart2, (uint8_t*)jsonBuf, strlen(jsonBuf), 100);
      seq++;
    }

    if (cmdReady == 1)
    {
      cmdReady = 0;
      if (strstr(rxBuffer, "\"a\":1"))
      {
        HAL_GPIO_WritePin(GPIOA, GPIO_PIN_5, GPIO_PIN_SET);
        Set_Servo_Angle(90);
        char ackMsg[] = "{\"ack\":1}\r\n";
        HAL_UART_Transmit(&huart2, (uint8_t*)ackMsg, strlen(ackMsg), 100);
      }
      else if (strstr(rxBuffer, "\"a\":0"))
      {
        HAL_GPIO_WritePin(GPIOA, GPIO_PIN_5, GPIO_PIN_RESET);
        Set_Servo_Angle(0);
        char ackMsg[] = "{\"ack\":1}\r\n";
        HAL_UART_Transmit(&huart2, (uint8_t*)ackMsg, strlen(ackMsg), 100);
      }
      memset(rxBuffer, 0, sizeof(rxBuffer));
      rxIndex = 0;
    }
  }
}

// ISR Callbacks (Minimal footprint)
void HAL_TIM_PeriodElapsedCallback(TIM_HandleTypeDef *htim) { if (htim->Instance == TIM2) read_sensors_flag = 1; }
void HAL_UART_RxCpltCallback(UART_HandleTypeDef *huart) {
  if (huart->Instance == USART2) {
    if (rxByte == '\n') cmdReady = (cmdReady == 0) ? 1 : 0;
    else if (cmdReady == 0) { rxBuffer[rxIndex++] = rxByte; if (rxIndex >= 64) rxIndex = 0; }
    HAL_UART_Receive_IT(&huart2, &rxByte, 1);
  }
}

void Read_DHT11(void)
{
  uint8_t Rh1=0, Rh2=0, T1=0, T2=0, SUM=0;
  DHT11_Start();
  if (DHT11_Check_Response() == 1) {
    Rh1 = DHT11_Read_Byte(); Rh2 = DHT11_Read_Byte();
    T1 = DHT11_Read_Byte(); T2 = DHT11_Read_Byte();
    SUM = DHT11_Read_Byte();
    if (SUM == (uint8_t)(Rh1 + Rh2 + T1 + T2)) { temperature = T1; humidity = Rh1; }
  }
  __enable_irq();
}

void Set_Servo_Angle(int angle) { uint16_t pulse = 1000 + (angle * 1000 / 180); __HAL_TIM_SET_COMPARE(&htim3, TIM_CHANNEL_1, pulse); }
void delay_us(uint16_t us) { __HAL_TIM_SET_COUNTER(&htim11, 0); while (__HAL_TIM_GET_COUNTER(&htim11) < us); }
void DHT11_Start(void) { 
  GPIO_InitTypeDef g = { .Pin = DHT11_PIN, .Mode = GPIO_MODE_OUTPUT_PP, .Speed = GPIO_SPEED_FREQ_LOW };
  HAL_GPIO_Init(DHT11_PORT, &g);
  HAL_GPIO_WritePin(DHT11_PORT, DHT11_PIN, GPIO_PIN_RESET); HAL_Delay(18);
  __disable_irq(); 
  HAL_GPIO_WritePin(DHT11_PORT, DHT11_PIN, GPIO_PIN_SET); delay_us(20);
  g.Mode = GPIO_MODE_INPUT; g.Pull = GPIO_NOPULL; HAL_GPIO_Init(DHT11_PORT, &g);
}
// (Include remaining standard MX_ Init functions from previous versions)
