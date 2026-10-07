#pragma once
#include <stdbool.h>
#include <stdint.h>

void bluetooth_transport_start(void);
void bluetooth_transport_allow_pairing(void);
bool bluetooth_transport_ready(void);
bool bluetooth_transport_selected(void);
bool bluetooth_transport_select(const char *mode);
const char *bluetooth_transport_mode(void);
bool bluetooth_transport_send_event(const char *line);
bool bluetooth_transport_send_report(uint8_t modifier, const uint8_t *keys);
void bluetooth_transport_status(char *out, unsigned capacity);
