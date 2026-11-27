export type TableState = "occupied" | "occupied_with_items" | "available" | "review";

export interface Bounds {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface StateSegment {
  startSeconds: number;
  endSeconds: number;
  state: TableState;
}

export interface ServiceApproach {
  startSeconds: number;
  endSeconds: number;
  durationSeconds: number;
}

export interface TableServiceMetrics {
  firstSeatedSeconds?: number | null;
  firstContactSeconds?: number | null;
  timeToFirstContactSeconds?: number | null;
  isFirstContactSlaViolated: boolean;
  totalApproachesCount: number;
  approaches: ServiceApproach[];
  totalVisitDurationSeconds?: number | null;
}

export interface DetectedTable {
  id: string;
  bounds: Bounds;
  segments: StateSegment[];
  serviceMetrics?: TableServiceMetrics;
}

export type PolygonPoint = [number, number];

export interface RestaurantTableStatus {
  id: string;
  name: string;
  count: number;
  max: number;
  state: "occupied" | "available";
  occupancy_seconds: number | null;
  occupancy_formatted: string;
}

export interface RestaurantStatus {
  total_tables: number;
  occupied_tables: number;
  occupancy_rate: number;
  seated_guests: number;
  total_capacity: number;
  capacity_rate: number;
  video_time: number;
  video_time_formatted: string;
  tables: RestaurantTableStatus[];
}

export interface TableZone {
  id: string;
  name: string;
  capacity: number;
  displayPolygon: PolygonPoint[];
  countingPolygon: PolygonPoint[];
  isActive?: boolean;
}

export interface TableConfiguration {
  id: string;
  bounds: Bounds;
  seatingZones: Bounds[];
}

export interface TableOrderInterval {
  tableId: string;
  startSeconds: number;
  endSeconds?: number | null;
  orderNumber?: string;
  guestsCount?: number;
}

export interface AnalysisResult {
  durationSeconds: number;
  sessionId?: string;
  videoUrl?: string;
  tables: DetectedTable[];
}

export type OrderStatus = "Open" | "Paid" | "Closed" | "Cancelled" | 0 | 1 | 2 | 3;
export type ItemPreparationStatus = "Ordered" | "Cooking" | "Ready" | "Served" | 0 | 1 | 2 | 3;

export interface OrderItem {
  itemId: string;
  dishName: string;
  category: string;
  quantity: number;
  price: number;
  course: number;
  status: ItemPreparationStatus;
  createdAt: string;
  servedAt?: string | null;
  videoPositionSeconds?: number | null;
}

export interface OrderDetails {
  orderId: string;
  orderNumber: string;
  restaurantId: string;
  tableId: string;
  sessionId?: string | null;
  waiterId?: string | null;
  waiterName?: string | null;
  guestsCount: number;
  status: OrderStatus;
  totalAmount: number;
  paidAmount: number;
  openedAt: string;
  firstDishServedAt?: string | null;
  paidAt?: string | null;
  closedAt?: string | null;
  cancelledAt?: string | null;
  cancellationReason?: string | null;
  videoPositionSeconds?: number | null;
  paidVideoPositionSeconds?: number | null;
  closedVideoPositionSeconds?: number | null;
  version: number;
  updatedAt: string;
  items: OrderItem[];
}

export interface OpenOrderPayload {
  orderId: string;
  tableId: string;
  restaurantId?: string;
  orderNumber?: string;
  sessionId?: string;
  waiterId?: string;
  waiterName?: string;
  guestsCount?: number;
  videoPositionSeconds?: number;
  openedAt?: string;
}

export interface AddOrderItemPayload {
  itemId: string;
  dishName: string;
  category?: string;
  quantity?: number;
  price?: number;
  course?: number;
  videoPositionSeconds?: number;
  timestamp?: string;
}

