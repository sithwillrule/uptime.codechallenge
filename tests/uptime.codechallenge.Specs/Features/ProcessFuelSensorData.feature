Feature: ProcessFuelSensorData

Fuel sensor data are processed in this feature
Example data is first day of the dataset provided in the excel

Scenario: Process fuel sensor data
	Given the example excel file is readed and dispatched to kafka topic
		| ServerDateTime                      | AnalogIN1 |
		| "2024-12-25 12:47:23.000000 +00:00" | 4114      |
	When fuel sensor interpeter is calibrated, noise is cleaned and data is stored
	Then From date '2024-12-25 12:47:23' TO '2025-02-12 10:55:26' is ready for query


