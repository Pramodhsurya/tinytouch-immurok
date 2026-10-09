// tinyTouch Bluetooth LE HID and authenticated companion transport.
// Passwords are never stored here; the existing nonce/HMAC exchange gates typing.
#include "bluetooth_transport.h"
#include "bluetooth_pairing_policy.h"
#include "transport_policy.h"
#include "usb_ccid.h"
#include "touch_pin_hid.h"
#include "usb_descriptors.h"
#include "tusb.h"
#include "esp_err.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "nimble/nimble_port.h"
#include "nimble/nimble_port_freertos.h"
#include "host/ble_hs.h"
#include "host/util/util.h"
#include "services/gap/ble_svc_gap.h"
#include "services/gatt/ble_svc_gatt.h"
#include "nvs.h"
#include <stdio.h>
#include <string.h>

// Nordic UART UUID layout; only authenticated PW/PW2 responses are accepted.
static const ble_uuid128_t svc_uuid = BLE_UUID128_INIT(
  0x9e,0xca,0xdc,0x24,0x0e,0xe5,0xa9,0xe0,0x93,0xf3,0xa3,0xb5,0x01,0x00,0x40,0x6e);
static const ble_uuid128_t rx_uuid = BLE_UUID128_INIT(
  0x9e,0xca,0xdc,0x24,0x0e,0xe5,0xa9,0xe0,0x93,0xf3,0xa3,0xb5,0x02,0x00,0x40,0x6e);
static const ble_uuid128_t tx_uuid = BLE_UUID128_INIT(
  0x9e,0xca,0xdc,0x24,0x0e,0xe5,0xa9,0xe0,0x93,0xf3,0xa3,0xb5,0x03,0x00,0x40,0x6e);
static const ble_uuid128_t id_uuid = BLE_UUID128_INIT(
  0x9e,0xca,0xdc,0x24,0x0e,0xe5,0xa9,0xe0,0x93,0xf3,0xa3,0xb5,0x04,0x00,0x40,0x6e);
static volatile uint16_t connection = BLE_HS_CONN_HANDLE_NONE;
static volatile bool encrypted, report_subscribed, boot_subscribed, event_subscribed;
static volatile uint32_t generation;
static uint16_t report_handle, boot_handle, event_handle;
static uint8_t own_addr_type;
static int64_t pairing_until;
static int security_result, encryption_result;
static uint8_t protocol_mode = 1;
static uint8_t last_report[8];
static uint8_t keyboard_leds;
static char rx_line[640];
static unsigned rx_used;
static bool rx_overflow;
// AUTO prefers an active USB host. BLE can be selected while USB supplies power.
static volatile uint8_t selected_mode; // 0=AUTO, 1=USB, 2=BLE
static const uint8_t report_map[] = {TUD_HID_REPORT_DESC_KEYBOARD(HID_REPORT_ID(1))};
void ble_store_config_init(void);

bool bluetooth_transport_ready(void) {
  return connection != BLE_HS_CONN_HANDLE_NONE && encrypted && event_subscribed &&
         (protocol_mode ? report_subscribed : boot_subscribed);
}
bool bluetooth_transport_selected(void) {
  return transport_policy_uses_ble(selected_mode, tud_mounted(), tud_suspended(),
                                   usb_ccid_vbus_sense_available(), usb_ccid_vbus_present());
}
const char *bluetooth_transport_mode(void) {
  return selected_mode == 2 ? "BLE" : selected_mode == 1 ? "USB" : "AUTO";
}
bool bluetooth_transport_select(const char *mode) {
  uint8_t value;
  if (!strcmp(mode,"AUTO")) value=0;
  else if (!strcmp(mode,"USB")) value=1;
  else if (!strcmp(mode,"BLE")) value=2;
  else return false;
  nvs_handle_t store;
  if (nvs_open("tt_bluetooth",NVS_READWRITE,&store) != ESP_OK) return false;
  esp_err_t rc=nvs_set_u8(store,"transport",value);
  if (rc==ESP_OK) rc=nvs_commit(store);
  nvs_close(store);
  if (rc!=ESP_OK) return false;
  touch_pin_hid_usb_detached(); // Cancels any pending auth before switching.
  selected_mode=value;
  return true;
}
void bluetooth_transport_allow_pairing(void) {
  // Called only by a USB console command after enrolled-finger authorization.
  pairing_until = esp_timer_get_time() + 180LL * 1000000LL;
  touch_pin_hid_log_event("bt_pairing_opened", 180);
  if (connection != BLE_HS_CONN_HANDLE_NONE && !encrypted)
    (void)ble_gap_terminate(connection, BLE_ERR_REM_USER_CONN_TERM);
}
void bluetooth_transport_status(char *out, unsigned capacity) {
  snprintf(out,capacity,"OK BT name=tinyTouch mode=%s connected=%u encrypted=%u keyboard=%u helper=%u ready=%u pairing=%u sm_rc=%d enc_rc=%d usb_mounted=%u usb_suspended=%u vbus_sense=%u vbus_present=%u active=%s",
    bluetooth_transport_mode(),connection!=BLE_HS_CONN_HANDLE_NONE,encrypted,
    report_subscribed||boot_subscribed,event_subscribed,bluetooth_transport_ready(),
    esp_timer_get_time()<pairing_until,security_result,encryption_result,
    tud_mounted(),tud_suspended(),usb_ccid_vbus_sense_available(),usb_ccid_vbus_present(),
    bluetooth_transport_selected() ? "BLE" : "USB");
}

static int append(struct ble_gatt_access_ctxt *ctx,const void *data,unsigned len) {
  return os_mbuf_append(ctx->om,data,len)==0 ? 0 : BLE_ATT_ERR_INSUFFICIENT_RES;
}
static int access_value(uint16_t conn,uint16_t handle,struct ble_gatt_access_ctxt *ctx,void *arg) {
  (void)conn; (void)handle;
  unsigned kind=(unsigned)(uintptr_t)arg;
  if (ctx->op==BLE_GATT_ACCESS_OP_READ_DSC) {
    const uint8_t ref[2]={1,kind==8 ? 2 : 1};
    touch_pin_hid_log_event("bt_report_ref",kind);
    return append(ctx,ref,2);
  }
  if (ctx->op==BLE_GATT_ACCESS_OP_READ_CHR) {
    const uint8_t info[]={0x11,0x01,0,0x02};
    const uint8_t pnp[]={2,0x3a,0x30,0x01,0x40,0x00,0x01};
    const uint8_t battery=100;
    switch(kind) {
      case 1: return append(ctx,info,sizeof(info));
      case 2:
        touch_pin_hid_log_event("bt_report_map",sizeof(report_map));
        return append(ctx,report_map,sizeof(report_map));
      case 3: return append(ctx,last_report,sizeof(last_report));
      case 4: return append(ctx,&protocol_mode,1);
      case 6: return append(ctx,tiny_touch_string_descriptors[3],strlen(tiny_touch_string_descriptors[3]));
      case 9: return append(ctx,pnp,sizeof(pnp));
      case 10: return append(ctx,&battery,1);
      case 8: return append(ctx,&keyboard_leds,1);
      default: return BLE_ATT_ERR_READ_NOT_PERMITTED;
    }
  }
  if (ctx->op!=BLE_GATT_ACCESS_OP_WRITE_CHR) return BLE_ATT_ERR_UNLIKELY;
  unsigned len=OS_MBUF_PKTLEN(ctx->om);
  if (kind==4) {
    uint8_t mode;
    if (len!=1 || ble_hs_mbuf_to_flat(ctx->om,&mode,1,NULL)!=0 || mode>1)
      return BLE_ATT_ERR_INVALID_ATTR_VALUE_LEN;
    if (mode!=protocol_mode) { generation++; touch_pin_hid_usb_detached(); }
    protocol_mode=mode;
    return 0;
  }
  if (kind==8) {
    if (len!=1 || ble_hs_mbuf_to_flat(ctx->om,&keyboard_leds,1,NULL)!=0)
      return BLE_ATT_ERR_INVALID_ATTR_VALUE_LEN;
    return 0;
  }
  if (kind!=5) return 0; // HID control point; no authentication operation.
  if (!encrypted || !bluetooth_transport_selected()) return BLE_ATT_ERR_INSUFFICIENT_AUTHEN;
  uint8_t chunk[512];
  if (len>sizeof(chunk) || ble_hs_mbuf_to_flat(ctx->om,chunk,sizeof(chunk),NULL)!=0)
    return BLE_ATT_ERR_INVALID_ATTR_VALUE_LEN;
  for (unsigned i=0;i<len;i++) {
    if (chunk[i]=='\r') continue;
    if (chunk[i]=='\n') {
      rx_line[rx_used]=0;
      if (!rx_overflow && rx_used) (void)touch_pin_hid_submit_response(rx_line);
      memset(rx_line,0,sizeof(rx_line)); rx_used=0; rx_overflow=false;
    } else if (rx_used+1<sizeof(rx_line)) rx_line[rx_used++]=(char)chunk[i];
    else rx_overflow=true;
  }
  memset(chunk,0,sizeof(chunk));
  return 0;
}

#define CHR(uuid16,kind,flags_) {.uuid=BLE_UUID16_DECLARE(uuid16),.access_cb=access_value,.arg=(void *)(uintptr_t)(kind),.flags=(flags_)}
#define REF(kind) (struct ble_gatt_dsc_def[]){{.uuid=BLE_UUID16_DECLARE(0x2908),.att_flags=BLE_ATT_F_READ,.access_cb=access_value,.arg=(void *)(uintptr_t)(kind)},{0}}
static const struct ble_gatt_svc_def services[]={
  {.type=BLE_GATT_SVC_TYPE_PRIMARY,.uuid=BLE_UUID16_DECLARE(0x1812),
   .characteristics=(struct ble_gatt_chr_def[]){
    CHR(0x2a4a,1,BLE_GATT_CHR_F_READ),
    CHR(0x2a4b,2,BLE_GATT_CHR_F_READ),
    {.uuid=BLE_UUID16_DECLARE(0x2a4d),.access_cb=access_value,.arg=(void *)3,
     .flags=BLE_GATT_CHR_F_READ|BLE_GATT_CHR_F_READ_ENC|BLE_GATT_CHR_F_NOTIFY,
     .val_handle=&report_handle,.descriptors=REF(7)},
    {.uuid=BLE_UUID16_DECLARE(0x2a4d),.access_cb=access_value,.arg=(void *)8,
     .flags=BLE_GATT_CHR_F_READ|BLE_GATT_CHR_F_READ_ENC|BLE_GATT_CHR_F_WRITE|BLE_GATT_CHR_F_WRITE_NO_RSP|BLE_GATT_CHR_F_WRITE_ENC,.descriptors=REF(8)},
    CHR(0x2a4e,4,BLE_GATT_CHR_F_READ|BLE_GATT_CHR_F_WRITE_NO_RSP),
    CHR(0x2a4c,7,BLE_GATT_CHR_F_WRITE_NO_RSP),
    {.uuid=BLE_UUID16_DECLARE(0x2a22),.access_cb=access_value,.arg=(void *)3,
     .flags=BLE_GATT_CHR_F_READ|BLE_GATT_CHR_F_READ_ENC|BLE_GATT_CHR_F_NOTIFY,.val_handle=&boot_handle},
    CHR(0x2a32,8,BLE_GATT_CHR_F_READ|BLE_GATT_CHR_F_READ_ENC|BLE_GATT_CHR_F_WRITE|BLE_GATT_CHR_F_WRITE_NO_RSP|BLE_GATT_CHR_F_WRITE_ENC),{0}}},
  {.type=BLE_GATT_SVC_TYPE_PRIMARY,.uuid=BLE_UUID16_DECLARE(0x180a),
   .characteristics=(struct ble_gatt_chr_def[]){CHR(0x2a50,9,BLE_GATT_CHR_F_READ),{0}}},
  {.type=BLE_GATT_SVC_TYPE_PRIMARY,.uuid=BLE_UUID16_DECLARE(0x180f),
   .characteristics=(struct ble_gatt_chr_def[]){CHR(0x2a19,10,BLE_GATT_CHR_F_READ),{0}}},
  {.type=BLE_GATT_SVC_TYPE_PRIMARY,.uuid=&svc_uuid.u,
   .characteristics=(struct ble_gatt_chr_def[]){
    {.uuid=&rx_uuid.u,.access_cb=access_value,.arg=(void *)5,.flags=BLE_GATT_CHR_F_WRITE|BLE_GATT_CHR_F_WRITE_ENC},
    {.uuid=&tx_uuid.u,.access_cb=access_value,.arg=(void *)7,.flags=BLE_GATT_CHR_F_NOTIFY,.val_handle=&event_handle},
    {.uuid=&id_uuid.u,.access_cb=access_value,.arg=(void *)6,.flags=BLE_GATT_CHR_F_READ|BLE_GATT_CHR_F_READ_ENC},{0}}},
  {0}
};

static int gap_event(struct ble_gap_event *event,void *arg);
static void advertise(void) {
  struct ble_hs_adv_fields fields={0};
  fields.flags=BLE_HS_ADV_F_DISC_GEN|BLE_HS_ADV_F_BREDR_UNSUP;
  fields.name=(uint8_t *)"tinyTouch"; fields.name_len=9; fields.name_is_complete=1;
  fields.appearance=0x03c1; fields.appearance_is_present=1;
  ble_uuid16_t hid=BLE_UUID16_INIT(0x1812);
  fields.uuids16=&hid; fields.num_uuids16=1; fields.uuids16_is_complete=0;
  if (ble_gap_adv_set_fields(&fields)) return;
  struct ble_hs_adv_fields response={0};
  response.uuids128=(ble_uuid128_t *)&svc_uuid; response.num_uuids128=1; response.uuids128_is_complete=0;
  (void)ble_gap_adv_rsp_set_fields(&response);
  struct ble_gap_adv_params params={.conn_mode=BLE_GAP_CONN_MODE_UND,.disc_mode=BLE_GAP_DISC_MODE_GEN};
  int rc=ble_gap_adv_start(own_addr_type,NULL,BLE_HS_FOREVER,&params,gap_event,NULL);
  if (rc) ESP_LOGW("tinyTouch","BLE advertise failed: %d",rc);
}
static void reset_connection(void) {
  encrypted=report_subscribed=boot_subscribed=event_subscribed=false;
  generation++; rx_used=0; rx_overflow=false;
  memset(rx_line,0,sizeof(rx_line)); memset(last_report,0,sizeof(last_report));
  touch_pin_hid_usb_detached();
}
static int gap_event(struct ble_gap_event *event,void *arg) {
  (void)arg;
  switch(event->type) {
    case BLE_GAP_EVENT_CONNECT:
      if (event->connect.status==0) {
        reset_connection(); connection=event->connect.conn_handle; protocol_mode=1;
        security_result=ble_gap_security_initiate(connection);
        touch_pin_hid_log_event("bt_sm_requested",security_result);
      } else advertise();
      break;
    case BLE_GAP_EVENT_DISCONNECT:
      connection=BLE_HS_CONN_HANDLE_NONE; reset_connection(); advertise(); break;
    case BLE_GAP_EVENT_ENC_CHANGE: {
      struct ble_gap_conn_desc desc;
      encryption_result=event->enc_change.status;
      touch_pin_hid_log_event("bt_enc_change",encryption_result);
      encrypted=event->enc_change.status==0 && ble_gap_conn_find(event->enc_change.conn_handle,&desc)==0 && desc.sec_state.encrypted;
      if (!encrypted) {
        reset_connection();
        // A failed security exchange can leave a live but unusable GATT link.
        // Close it so both the keyboard host and companion can reconnect.
        // Bonded peers retry encryption normally; never downgrade readiness.
        int rc=ble_gap_terminate(event->enc_change.conn_handle,
                                 BLE_ERR_REM_USER_CONN_TERM);
        touch_pin_hid_log_event("bt_security_reconnect",rc);
      }
      break;
    }
    case BLE_GAP_EVENT_SUBSCRIBE:
      if (event->subscribe.attr_handle==report_handle) report_subscribed=event->subscribe.cur_notify;
      if (event->subscribe.attr_handle==boot_handle) boot_subscribed=event->subscribe.cur_notify;
      if (event->subscribe.attr_handle==event_handle) event_subscribed=event->subscribe.cur_notify;
      if (!bluetooth_transport_ready()) { generation++; touch_pin_hid_usb_detached(); }
      break;
    case BLE_GAP_EVENT_ADV_COMPLETE: advertise(); break;
    case BLE_GAP_EVENT_REPEAT_PAIRING: {
      struct ble_gap_conn_desc desc;
      if (!bluetooth_repair_allowed(esp_timer_get_time(), pairing_until,
                                    event->repeat_pairing.new_sc, event->repeat_pairing.new_key_size) ||
          ble_gap_conn_find(event->repeat_pairing.conn_handle,&desc) != 0) {
        touch_pin_hid_log_event("bt_repeat_blocked",0);
        return BLE_GAP_REPEAT_PAIRING_IGNORE;
      }
      int rc=ble_store_util_delete_peer(&desc.peer_id_addr);
      touch_pin_hid_log_event("bt_repeat_allowed",rc);
      return rc == 0 ? BLE_GAP_REPEAT_PAIRING_RETRY : BLE_GAP_REPEAT_PAIRING_IGNORE;
    }
    default: break;
  }
  return 0;
}
static bool notify(uint16_t handle,const void *data,unsigned len,uint16_t conn,uint32_t gen) {
  for (unsigned i=0;i<20;i++) {
    if (connection!=conn || generation!=gen || !bluetooth_transport_ready()) return false;
    struct os_mbuf *packet=ble_hs_mbuf_from_flat(data,len);
    if (!packet) return false;
    int rc=ble_gatts_notify_custom(conn,handle,packet);
    if (!rc) return true;
    if (rc!=BLE_HS_ENOMEM && rc!=BLE_HS_EBUSY && rc!=BLE_HS_EAGAIN) return false;
    vTaskDelay(pdMS_TO_TICKS(10));
  }
  return false;
}
bool bluetooth_transport_send_report(uint8_t modifier,const uint8_t *keys) {
  uint16_t conn=connection; uint32_t gen=generation;
  uint8_t report[8]={modifier,0};
  if (keys) memcpy(report+2,keys,6);
  if (!notify(protocol_mode ? report_handle : boot_handle,report,8,conn,gen)) return false;
  memcpy(last_report,report,8);
  // Keep press and release in distinct BLE connection intervals.
  vTaskDelay(pdMS_TO_TICKS(30));
  return connection==conn && generation==gen && bluetooth_transport_ready();
}
bool bluetooth_transport_send_event(const char *line) {
  uint16_t conn=connection; uint32_t gen=generation;
  unsigned length=strlen(line),payload=ble_att_mtu(conn);
  if (payload<=3) return false;
  payload-=3;
  for (unsigned at=0;at<length;) {
    unsigned count=length-at<payload ? length-at : payload;
    if (!notify(event_handle,line+at,count,conn,gen)) return false;
    at+=count;
  }
  return notify(event_handle,"\n",1,conn,gen);
}
static void synced(void) {
  if (ble_hs_util_ensure_addr(0)==0 && ble_hs_id_infer_auto(0,&own_addr_type)==0) advertise();
}
static void host_task(void *arg) { (void)arg; nimble_port_run(); nimble_port_freertos_deinit(); }
void bluetooth_transport_start(void) {
  nvs_handle_t store;
  if (nvs_open("tt_bluetooth",NVS_READONLY,&store)==ESP_OK) {
    uint8_t value=0; if (nvs_get_u8(store,"transport",&value)==ESP_OK && value<=2) selected_mode=value;
    nvs_close(store);
  }
  ESP_ERROR_CHECK(nimble_port_init());
  ble_hs_cfg.sync_cb=synced;
  ble_hs_cfg.store_status_cb=ble_store_util_status_rr;
  ble_hs_cfg.sm_io_cap=BLE_HS_IO_NO_INPUT_OUTPUT;
  ble_hs_cfg.sm_bonding=1; ble_hs_cfg.sm_sc=1;
  ble_hs_cfg.sm_our_key_dist=BLE_SM_PAIR_KEY_DIST_ENC|BLE_SM_PAIR_KEY_DIST_ID;
  ble_hs_cfg.sm_their_key_dist=BLE_SM_PAIR_KEY_DIST_ENC|BLE_SM_PAIR_KEY_DIST_ID;
  ble_svc_gap_init(); ble_svc_gatt_init();
  assert(ble_gatts_count_cfg(services)==0);
  assert(ble_gatts_add_svcs(services)==0);
  assert(ble_svc_gap_device_name_set("tinyTouch")==0);
  ble_svc_gap_device_appearance_set(0x03c1);
  ble_att_set_preferred_mtu(256);
  ble_store_config_init();
  nimble_port_freertos_init(host_task);
}
